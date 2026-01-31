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
/// Generates DI registration code and optimized typed dispatch to eliminate runtime reflection.
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

        // Find all notification types to capture their options
        var notificationDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsNotificationCandidate(node),
                transform: static (context, ct) => GetNotificationInfo(context, ct))
            .Where(static info => info is not null);

        // Combine with compilation
        var compilationAndHandlers = context.CompilationProvider
            .Combine(handlerDeclarations.Collect())
            .Combine(notificationDeclarations.Collect());

        // Generate the output
        context.RegisterSourceOutput(compilationAndHandlers, static (spc, source) =>
        {
            var ((compilation, handlers), notifications) = source;
            Execute(spc, compilation, handlers!, notifications!);
        });
    }

    private static bool IsHandlerCandidate(SyntaxNode node)
    {
        // Look for class declarations that might implement handler interfaces
        return node is ClassDeclarationSyntax classDecl
               && classDecl.BaseList is not null
               && !classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);
    }

    private static bool IsNotificationCandidate(SyntaxNode node)
    {
        // Look for class/record declarations that might implement INotification
        return node is TypeDeclarationSyntax typeDecl
               && typeDecl.BaseList is not null;
    }

    private static NotificationTypeInfo? GetNotificationInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;

        if (typeSymbol is null)
            return null;

        // Check if it implements INotification
        var implementsNotification = typeSymbol.AllInterfaces
            .Any(i => i.ToDisplayString() == "MediatorLite.INotification");

        if (!implementsNotification)
            return null;

        // Get NotificationOptionsAttribute if present
        var optionsAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "NotificationOptionsAttribute");

        if (optionsAttr == null)
            return null;

        int executionStrategy = 0; // Sequential
        int errorStrategy = 1; // ContinueAndAggregate
        bool overrideGlobal = true;

        foreach (var arg in optionsAttr.NamedArguments)
        {
            if (arg.Key == "ExecutionStrategy" && arg.Value.Value is int es)
                executionStrategy = es;
            else if (arg.Key == "ErrorStrategy" && arg.Value.Value is int ers)
                errorStrategy = ers;
            else if (arg.Key == "OverrideGlobal" && arg.Value.Value is bool og)
                overrideGlobal = og;
        }

        if (!overrideGlobal)
            return null;

        return new NotificationTypeInfo(
            TypeName: typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ExecutionStrategy: executionStrategy,
            ErrorStrategy: errorStrategy);
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

        // Get handler order from NotificationHandlerOrderAttribute
        int? handlerOrder = null;
        var orderAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "NotificationHandlerOrderAttribute");
        if (orderAttr != null && orderAttr.ConstructorArguments.Length > 0)
        {
            handlerOrder = orderAttr.ConstructorArguments[0].Value as int?;
        }

        var requestHandlerInterfaces = new List<HandlerInterfaceInfo>();
        var notificationHandlerInterfaces = new List<NotificationHandlerInterfaceInfo>();

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
                    notificationHandlerInterfaces.Add(new NotificationHandlerInterfaceInfo(
                        InterfaceType: iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        NotificationType: typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Order: handlerOrder));
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
        ImmutableArray<HandlerInfo?> handlers,
        ImmutableArray<NotificationTypeInfo?> notifications)
    {
        var validHandlers = handlers.Where(h => h is not null).Cast<HandlerInfo>().ToList();
        var validNotifications = notifications.Where(n => n is not null).Cast<NotificationTypeInfo>().ToList();

        if (validHandlers.Count == 0)
        {
            // Still generate the extension method even if no handlers found
            GenerateEmptyRegistration(context);
            return;
        }

        GenerateRegistrationCode(context, validHandlers);
        GenerateSourceGeneratedMediator(context, validHandlers, validNotifications);
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

                             /// <summary>
                             /// Gets the number of request handlers discovered at compile time.
                             /// </summary>
                             public static int RequestHandlerCount => 0;

                             /// <summary>
                             /// Gets the number of notification handlers discovered at compile time.
                             /// </summary>
                             public static int NotificationHandlerCount => 0;
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
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
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
        sb.AppendLine(
            "        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedHandlers(");
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
        sb.AppendLine("            // Register the source-generated mediator for zero-reflection dispatch");
        sb.AppendLine("            services.AddSingleton<global::MediatorLite.ISourceGeneratedMediator, SourceGeneratedMediator>();");
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

    /// <summary>
    /// Generates the ISourceGeneratedMediator implementation with pattern matching dispatch.
    /// </summary>
    private static void GenerateSourceGeneratedMediator(
        SourceProductionContext context,
        List<HandlerInfo> handlers,
        List<NotificationTypeInfo> notifications)
    {
        var requestHandlers = handlers.SelectMany(h =>
            h.RequestHandlers.Select(r => (Handler: h, Interface: r))).ToList();

        var notificationHandlers = handlers.SelectMany(h =>
            h.NotificationHandlers.Select(n => (Handler: h, Interface: n))).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace MediatorLite.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Source-generated mediator implementation that provides zero-reflection dispatch");
        sb.AppendLine("    /// for compile-time discovered handlers.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public sealed class SourceGeneratedMediator : global::MediatorLite.ISourceGeneratedMediator");
        sb.AppendLine("    {");

        // Generate TrySendAsync method
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public ValueTask<TResponse>? TrySendAsync<TResponse>(");
        sb.AppendLine("            IServiceProvider serviceProvider,");
        sb.AppendLine("            global::MediatorLite.IRequest<TResponse> request,");
        sb.AppendLine("            CancellationToken cancellationToken)");
        sb.AppendLine("        {");

        if (requestHandlers.Count > 0)
        {
            sb.AppendLine("            return request switch");
            sb.AppendLine("            {");

            foreach (var (handler, iface) in requestHandlers)
            {
                var requestType = iface.RequestType;
                var responseType = iface.ResponseType!;

                sb.AppendLine($"                {requestType} r => DispatchAs<TResponse, {responseType}>(");
                sb.AppendLine($"                    serviceProvider.GetRequiredService<{iface.InterfaceType}>().HandleAsync(r, cancellationToken)),");
            }

            sb.AppendLine("                _ => null,");
            sb.AppendLine("            };");
        }
        else
        {
            sb.AppendLine("            return null;");
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        // Generate TryInvokeHandlerAsync method (same logic as TrySendAsync, for use in behavior pipelines)
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public ValueTask<TResponse>? TryInvokeHandlerAsync<TResponse>(");
        sb.AppendLine("            IServiceProvider serviceProvider,");
        sb.AppendLine("            global::MediatorLite.IRequest<TResponse> request,");
        sb.AppendLine("            CancellationToken cancellationToken)");
        sb.AppendLine("        {");

        if (requestHandlers.Count > 0)
        {
            sb.AppendLine("            return request switch");
            sb.AppendLine("            {");

            foreach (var (handler, iface) in requestHandlers)
            {
                var requestType = iface.RequestType;
                var responseType = iface.ResponseType!;

                sb.AppendLine($"                {requestType} r => DispatchAs<TResponse, {responseType}>(");
                sb.AppendLine($"                    serviceProvider.GetRequiredService<{iface.InterfaceType}>().HandleAsync(r, cancellationToken)),");
            }

            sb.AppendLine("                _ => null,");
            sb.AppendLine("            };");
        }
        else
        {
            sb.AppendLine("            return null;");
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        // Generate TryGetHandlerOrder method
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public int? TryGetHandlerOrder(Type handlerType)");
        sb.AppendLine("        {");

        var handlersWithOrder = notificationHandlers.Where(h => h.Interface.Order.HasValue).ToList();
        if (handlersWithOrder.Count > 0)
        {
            sb.AppendLine("            var fullName = handlerType.FullName;");
            sb.AppendLine("            return fullName switch");
            sb.AppendLine("            {");

            foreach (var (handler, iface) in handlersWithOrder)
            {
                var handlerTypeName = handler.ClassName.Replace("global::", "");
                sb.AppendLine($"                \"{handlerTypeName}\" => {iface.Order!.Value},");
            }

            sb.AppendLine("                _ => null,");
            sb.AppendLine("            };");
        }
        else
        {
            sb.AppendLine("            return null;");
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        // Generate TryGetNotificationOptions method
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public (global::MediatorLite.NotificationExecutionStrategy ExecutionStrategy, global::MediatorLite.NotificationErrorStrategy ErrorStrategy)? TryGetNotificationOptions(Type notificationType)");
        sb.AppendLine("        {");

        if (notifications.Count > 0)
        {
            sb.AppendLine("            var fullName = notificationType.FullName;");
            sb.AppendLine("            return fullName switch");
            sb.AppendLine("            {");

            foreach (var notification in notifications)
            {
                var typeName = notification.TypeName.Replace("global::", "");
                sb.AppendLine($"                \"{typeName}\" => ((global::MediatorLite.NotificationExecutionStrategy){notification.ExecutionStrategy}, (global::MediatorLite.NotificationErrorStrategy){notification.ErrorStrategy}),");
            }

            sb.AppendLine("                _ => null,");
            sb.AppendLine("            };");
        }
        else
        {
            sb.AppendLine("            return null;");
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        // Helper method to cast ValueTask<TActual> to ValueTask<TResponse>
        sb.AppendLine("        private static async ValueTask<TResponse> DispatchAs<TResponse, TActual>(ValueTask<TActual> task)");
        sb.AppendLine("        {");
        sb.AppendLine("            var result = await task.ConfigureAwait(false);");
        sb.AppendLine("            return (TResponse)(object)result!;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("SourceGeneratedMediator.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// Gets the simple type name from a fully qualified name.
    /// </summary>
    private static string GetSimpleTypeName(string fullyQualifiedType)
    {
        // Remove "global::" prefix if present
        var name = fullyQualifiedType.Replace("global::", "");

        // Get the last part after the last dot
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
        {
            name = name.Substring(lastDot + 1);
        }

        return name;
    }
}

internal sealed record HandlerInfo(
    string ClassName,
    string Namespace,
    List<HandlerInterfaceInfo> RequestHandlers,
    List<NotificationHandlerInterfaceInfo> NotificationHandlers);

internal sealed record HandlerInterfaceInfo(
    string InterfaceType,
    string RequestType,
    string? ResponseType);

internal sealed record NotificationHandlerInterfaceInfo(
    string InterfaceType,
    string NotificationType,
    int? Order);

internal sealed record NotificationTypeInfo(
    string TypeName,
    int ExecutionStrategy,
    int ErrorStrategy);
