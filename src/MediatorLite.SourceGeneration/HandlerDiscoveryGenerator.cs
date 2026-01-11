using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace MediatorLite.SourceGeneration;

/// <summary>
/// Source generator that discovers request handlers and notification handlers at compile time.
/// Generates DI registration code to eliminate runtime reflection.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class HandlerDiscoveryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all class declarations that might be handlers
        var handlerDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsHandlerCandidate(node),
                transform: static (context, ct) => GetHandlerInfo(context, ct))
            .Where(static info => info is not null);

        // Combine with compilation
        var compilationAndHandlers = context.CompilationProvider.Combine(handlerDeclarations.Collect());

        // Generate the output
        context.RegisterSourceOutput(compilationAndHandlers, static (spc, source) =>
        {
            var (compilation, handlers) = source;
            Execute(spc, compilation, handlers!);
        });
    }

    private static bool IsHandlerCandidate(SyntaxNode node)
    {
        // Look for class declarations that might implement handler interfaces
        return node is ClassDeclarationSyntax classDecl
            && classDecl.BaseList is not null
            && !classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);
    }

    private static HandlerInfo? GetHandlerInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;

        if (classSymbol is null || classSymbol.IsAbstract)
            return null;

        // Check if it has the Skip attribute
        var hasSkipAttribute = classSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "MediatorGenerationAttribute"
                && a.NamedArguments.Any(arg => arg.Key == "Skip" && arg.Value.Value is true));

        if (hasSkipAttribute)
            return null;

        var requestHandlerInterfaces = new List<HandlerInterfaceInfo>();
        var notificationHandlerInterfaces = new List<HandlerInterfaceInfo>();

        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (!iface.IsGenericType)
                continue;

            var originalDef = iface.OriginalDefinition.ToDisplayString();

            // Check for IRequestHandler<TRequest, TResponse>
            if (originalDef == "MediatorLite.IRequestHandler<TRequest, TResponse>")
            {
                var typeArgs = iface.TypeArguments;
                if (typeArgs.Length == 2)
                {
                    requestHandlerInterfaces.Add(new HandlerInterfaceInfo(
                        InterfaceType: iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        RequestType: typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ResponseType: typeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                }
            }
            // Check for INotificationHandler<TNotification>
            else if (originalDef == "MediatorLite.INotificationHandler<TNotification>")
            {
                var typeArgs = iface.TypeArguments;
                if (typeArgs.Length == 1)
                {
                    notificationHandlerInterfaces.Add(new HandlerInterfaceInfo(
                        InterfaceType: iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        RequestType: typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ResponseType: null));
                }
            }
        }

        if (requestHandlerInterfaces.Count == 0 && notificationHandlerInterfaces.Count == 0)
            return null;

        return new HandlerInfo(
            ClassName: classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace: classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
            RequestHandlers: requestHandlerInterfaces,
            NotificationHandlers: notificationHandlerInterfaces);
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<HandlerInfo?> handlers)
    {
        var validHandlers = handlers.Where(h => h is not null).Cast<HandlerInfo>().ToList();

        if (validHandlers.Count == 0)
        {
            // Still generate the extension method even if no handlers found
            GenerateEmptyRegistration(context);
            return;
        }

        GenerateRegistrationCode(context, validHandlers);
    }

    private static void GenerateEmptyRegistration(SourceProductionContext context)
    {
        var source = """
            // <auto-generated />
            #nullable enable

            namespace MediatorLite.Generated
            {
                /// <summary>
                /// Source-generated MediatorLite handler registrations.
                /// </summary>
                public static class MediatorLiteRegistration
                {
                    /// <summary>
                    /// Adds all source-generated handlers to the service collection.
                    /// </summary>
                    /// <param name="services">The service collection.</param>
                    /// <returns>The service collection for chaining.</returns>
                    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedHandlers(
                        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    {
                        // No handlers discovered at compile time
                        return services;
                    }
                }
            }
            """;

        context.AddSource("MediatorLiteRegistration.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static void GenerateRegistrationCode(
        SourceProductionContext context,
        List<HandlerInfo> handlers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace MediatorLite.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Source-generated MediatorLite handler registrations.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class MediatorLiteRegistration");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Adds all source-generated handlers to the service collection.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <param name=\"services\">The service collection.</param>");
        sb.AppendLine("        /// <returns>The service collection for chaining.</returns>");
        sb.AppendLine("        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedHandlers(");
        sb.AppendLine("            this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("        {");

        // Generate request handler registrations
        var requestHandlers = handlers.SelectMany(h =>
            h.RequestHandlers.Select(r => (Handler: h, Interface: r))).ToList();

        if (requestHandlers.Count > 0)
        {
            sb.AppendLine("            // Request Handlers");
            foreach (var (handler, iface) in requestHandlers)
            {
                sb.AppendLine($"            services.AddTransient<{iface.InterfaceType}, {handler.ClassName}>();");
            }
            sb.AppendLine();
        }

        // Generate notification handler registrations
        var notificationHandlers = handlers.SelectMany(h =>
            h.NotificationHandlers.Select(n => (Handler: h, Interface: n))).ToList();

        if (notificationHandlers.Count > 0)
        {
            sb.AppendLine("            // Notification Handlers");
            foreach (var (handler, iface) in notificationHandlers)
            {
                sb.AppendLine($"            services.AddTransient<{iface.InterfaceType}, {handler.ClassName}>();");
            }
        }

        sb.AppendLine();
        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Generate handler count properties for diagnostics
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Gets the number of request handlers discovered at compile time.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        public static int RequestHandlerCount => {requestHandlers.Count};");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Gets the number of notification handlers discovered at compile time.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        public static int NotificationHandlerCount => {notificationHandlers.Count};");

        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("MediatorLiteRegistration.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }
}

internal sealed record HandlerInfo(
    string ClassName,
    string Namespace,
    List<HandlerInterfaceInfo> RequestHandlers,
    List<HandlerInterfaceInfo> NotificationHandlers);

internal sealed record HandlerInterfaceInfo(
    string InterfaceType,
    string RequestType,
    string? ResponseType);
