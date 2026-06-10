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
    // Mirrors constants in src/MediatorLite/Diagnostics/MediatorDiagnostics.cs.
    // Kept in sync manually because the generator project (netstandard2.0) cannot
    // reference the runtime MediatorLite assembly. If the runtime constants change,
    // update these literals to match.
    private const string ActivityNameSendRequest = "MediatorLite.Send";
    private const string ActivityNamePublishNotification = "MediatorLite.Publish";

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

        // Find all behavior declarations
        var behaviorDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsBehaviorCandidate(node),
                transform: static (context, ct) => GetBehaviorInfo(context, ct))
            .Where(static info => info is not null);

        // Find all validator declarations
        var validatorDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsValidatorCandidate(node),
                transform: static (context, ct) => GetValidatorInfo(context, ct))
            .Where(static info => info is not null);

        // Assembly-level defaults for notification strategies (compile-time)
        var assemblyDefaults = context.CompilationProvider
            .Select(static (compilation, _) => GetAssemblyDefaults(compilation));

        // Combine with compilation
        var compilationAndData = context.CompilationProvider
            .Combine(handlerDeclarations.Collect())
            .Combine(notificationDeclarations.Collect())
            .Combine(behaviorDeclarations.Collect())
            .Combine(validatorDeclarations.Collect())
            .Combine(assemblyDefaults);

        // Generate the output
        context.RegisterSourceOutput(compilationAndData, static (spc, source) =>
        {
            var (((((compilation, handlers), notifications), behaviors), validators), defaults) = source;
            Execute(spc, compilation, handlers!, notifications!, behaviors!, validators!, defaults);
        });
    }

    /// <summary>
    /// Reads assembly-level defaults for notification strategies from
    /// <see cref="DefaultNotificationExecutionAttribute"/> and <see cref="DefaultNotificationErrorAttribute"/>.
    /// </summary>
    private static AssemblyDefaults GetAssemblyDefaults(Compilation compilation)
    {
        int? execution = null;
        int? error = null;
        bool loggingDisabled = false;
        bool tracingDisabled = false;

        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            var name = attr.AttributeClass?.Name;
            if (name == "DefaultNotificationExecutionAttribute"
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is int es)
            {
                execution = es;
            }
            else if (name == "DefaultNotificationErrorAttribute"
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is int ers)
            {
                error = ers;
            }
            else if (name == "DisableMediatorLoggingAttribute")
            {
                loggingDisabled = true;
            }
            else if (name == "DisableMediatorTracingAttribute")
            {
                tracingDisabled = true;
            }
        }

        return new AssemblyDefaults(execution, error, loggingDisabled, tracingDisabled);
    }

    /// <summary>
    /// Resolves the final (execution, error) strategy tuple for a notification type.
    /// Precedence per strategy: per-notification attribute &gt; assembly default &gt; library default
    /// (Sequential=0 for execution, StopOnFirstError=0 for error).
    /// </summary>
    private static (int Execution, int Error) ResolveStrategies(
        NotificationTypeInfo? perType,
        AssemblyDefaults globals)
    {
        int execution = perType?.ExecutionStrategy ?? globals.ExecutionStrategy ?? 0;
        int error = perType?.ErrorStrategy ?? globals.ErrorStrategy ?? 0;
        return (execution, error);
    }

    private static bool IsHandlerCandidate(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDecl
               && classDecl.BaseList is not null
               && !classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);
    }

    private static bool IsNotificationCandidate(SyntaxNode node)
    {
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

        var implementsNotification = typeSymbol.AllInterfaces
            .Any(i => i.ToDisplayString() == "MediatorLite.INotification");

        if (!implementsNotification)
            return null;

        int? executionStrategy = null;
        int? errorStrategy = null;

        foreach (var attr in typeSymbol.GetAttributes())
        {
            var name = attr.AttributeClass?.Name;
            if (name == "NotificationExecutionAttribute"
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is int es)
            {
                executionStrategy = es;
            }
            else if (name == "NotificationErrorAttribute"
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is int ers)
            {
                errorStrategy = ers;
            }
        }

        if (executionStrategy is null && errorStrategy is null)
            return null;

        return new NotificationTypeInfo(
            TypeName: typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ExecutionStrategy: executionStrategy,
            ErrorStrategy: errorStrategy);
    }

    private static bool IsBehaviorCandidate(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDecl
               && classDecl.BaseList is not null
               && !classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);
    }

    private static BehaviorInfo? GetBehaviorInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;

        if (classSymbol is null || classSymbol.IsAbstract)
            return null;

        var hasSkipAttribute = classSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "MediatorGenerationAttribute"
                      && a.NamedArguments.Any(arg => arg.Key == "Skip" && arg.Value.Value is true));

        if (hasSkipAttribute)
            return null;

        var behaviorInterfaces = new List<BehaviorInterfaceInfo>();
        bool isOpenGeneric = classSymbol.IsGenericType && classSymbol.IsUnboundGenericType == false
                             && classSymbol.TypeParameters.Length > 0;

        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (!iface.IsGenericType)
                continue;

            var originalDef = iface.OriginalDefinition.ToDisplayString();

            if (originalDef == "MediatorLite.IPipelineBehavior<TRequest, TResponse>")
            {
                var typeArgs = iface.TypeArguments;
                if (typeArgs.Length == 2)
                {
                    bool isInterfaceOpen = typeArgs.Any(t => t.TypeKind == TypeKind.TypeParameter);

                    behaviorInterfaces.Add(new BehaviorInterfaceInfo(
                        InterfaceType: iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        RequestType: isInterfaceOpen ? null : typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ResponseType: isInterfaceOpen ? null : typeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        IsOpenGeneric: isInterfaceOpen));
                }
            }
        }

        if (behaviorInterfaces.Count == 0)
            return null;

        // Extract BehaviorOrderAttribute if present
        int behaviorOrder = 0;
        var orderAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "BehaviorOrderAttribute");
        if (orderAttr != null && orderAttr.ConstructorArguments.Length > 0)
        {
            behaviorOrder = (int)(orderAttr.ConstructorArguments[0].Value ?? 0);
        }

        return new BehaviorInfo(
            ClassName: classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace: classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
            BehaviorInterfaces: behaviorInterfaces,
            IsOpenGeneric: isOpenGeneric,
            Order: behaviorOrder);
    }

    private static bool IsValidatorCandidate(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDecl
               && classDecl.BaseList is not null
               && !classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);
    }

    private static ValidatorInfo? GetValidatorInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;

        if (classSymbol is null || classSymbol.IsAbstract)
            return null;

        // Skip open generic validators (e.g., DataAnnotationsValidator<T> from the library)
        if (classSymbol.IsGenericType)
            return null;

        var hasSkipAttribute = classSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "MediatorGenerationAttribute"
                      && a.NamedArguments.Any(arg => arg.Key == "Skip" && arg.Value.Value is true));

        if (hasSkipAttribute)
            return null;

        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (!iface.IsGenericType)
                continue;

            var originalDef = iface.OriginalDefinition.ToDisplayString();

            if (originalDef == "MediatorLite.Validation.IValidator<TRequest>")
            {
                var typeArgs = iface.TypeArguments;
                if (typeArgs.Length == 1)
                {
                    // Only discover validators for concrete (non-generic) request types
                    if (typeArgs[0].TypeKind == TypeKind.TypeParameter)
                        continue;

                    return new ValidatorInfo(
                        ClassName: classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Namespace: classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                        InterfaceType: iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        RequestType: typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Collects the fully-qualified names of all base classes of a type (excluding object).
    /// Used to order switch arms most-derived-first so type-pattern dispatch preserves
    /// runtime-type specificity when message types inherit from each other.
    /// </summary>
    private static List<string> GetBaseTypeNames(ITypeSymbol type)
    {
        var result = new List<string>();
        var current = type.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            result.Add(current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            current = current.BaseType;
        }
        return result;
    }

    /// <summary>
    /// Orders items so that derived message types precede their base types. Within the set of
    /// dispatched types, an item's depth is the number of its base classes that are themselves
    /// dispatched; sorting by depth descending guarantees derived-before-base, which both keeps
    /// the emitted switch arms compilable (a base-type arm would subsume a later derived arm)
    /// and matches the runtime-type semantics of the previous GetType()-keyed dispatch.
    /// </summary>
    private static List<T> SortMostDerivedFirst<T>(
        List<T> items,
        Func<T, string> typeName,
        Func<T, List<string>?> baseTypes)
    {
        var dispatchedTypes = new HashSet<string>(items.Select(typeName));
        return items
            .Select((item, index) => (Item: item, Index: index,
                Depth: baseTypes(item)?.Count(dispatchedTypes.Contains) ?? 0))
            .OrderByDescending(x => x.Depth)
            .ThenBy(x => x.Index)
            .Select(x => x.Item)
            .ToList();
    }

    /// <summary>
    /// Checks whether a type has any properties decorated with DataAnnotation validation attributes.
    /// </summary>
    private static bool HasDataAnnotationAttributes(ITypeSymbol typeSymbol)
    {
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IPropertySymbol property)
            {
                foreach (var attr in property.GetAttributes())
                {
                    if (IsValidationAttribute(attr.AttributeClass))
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Checks whether an attribute type inherits from System.ComponentModel.DataAnnotations.ValidationAttribute.
    /// </summary>
    private static bool IsValidationAttribute(INamedTypeSymbol? attributeType)
    {
        var current = attributeType;
        while (current != null)
        {
            if (current.ToDisplayString() == "System.ComponentModel.DataAnnotations.ValidationAttribute")
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static HandlerInfo? GetHandlerInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;

        if (classSymbol is null || classSymbol.IsAbstract)
            return null;

        var hasSkipAttribute = classSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "MediatorGenerationAttribute"
                      && a.NamedArguments.Any(arg => arg.Key == "Skip" && arg.Value.Value is true));

        if (hasSkipAttribute)
            return null;

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

            if (originalDef == "MediatorLite.IRequestHandler<TRequest, TResponse>")
            {
                var typeArgs = iface.TypeArguments;
                if (typeArgs.Length == 2)
                {
                    bool hasDataAnnotations = HasDataAnnotationAttributes(typeArgs[0]);

                    requestHandlerInterfaces.Add(new HandlerInterfaceInfo(
                        InterfaceType: iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        RequestType: typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ResponseType: typeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        HasDataAnnotations: hasDataAnnotations,
                        BaseTypes: GetBaseTypeNames(typeArgs[0])));
                }
            }
            else if (originalDef == "MediatorLite.INotificationHandler<TNotification>")
            {
                var typeArgs = iface.TypeArguments;
                if (typeArgs.Length == 1)
                {
                    notificationHandlerInterfaces.Add(new NotificationHandlerInterfaceInfo(
                        InterfaceType: iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        NotificationType: typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Order: handlerOrder,
                        BaseTypes: GetBaseTypeNames(typeArgs[0])));
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
        ImmutableArray<NotificationTypeInfo?> notifications,
        ImmutableArray<BehaviorInfo?> behaviors,
        ImmutableArray<ValidatorInfo?> validators,
        AssemblyDefaults assemblyDefaults)
    {
        var validHandlers = handlers.Where(h => h is not null).Cast<HandlerInfo>().ToList();
        var validNotifications = notifications.Where(n => n is not null).Cast<NotificationTypeInfo>().ToList();
        var validBehaviors = behaviors.Where(b => b is not null).Cast<BehaviorInfo>().ToList();
        var validValidators = validators.Where(v => v is not null).Cast<ValidatorInfo>().ToList();

        if (validHandlers.Count == 0)
        {
            GenerateEmptyRegistration(context);
            return;
        }

        var expandedBehaviors = ExpandBehaviors(validBehaviors, validHandlers);

        // Determine which request types need validation
        var requestTypesWithValidation = DetermineValidationTargets(validHandlers, validValidators);

        // Add ValidationBehavior entries for InvokeBehavior dispatch
        foreach (var (requestType, responseType) in requestTypesWithValidation)
        {
            expandedBehaviors.Add(new ExpandedBehaviorInfo(
                BehaviorTypeName: $"global::MediatorLite.Validation.ValidationBehavior<{requestType}, {responseType}>",
                RequestType: requestType,
                ResponseType: responseType,
                InterfaceType: $"global::MediatorLite.IPipelineBehavior<{requestType}, {responseType}>"));
        }

        GenerateRegistrationCode(context, validHandlers, expandedBehaviors, validValidators, requestTypesWithValidation);
        GenerateSourceGeneratedMediator(context, validHandlers, validNotifications, validBehaviors, expandedBehaviors, assemblyDefaults);
    }

    /// <summary>
    /// Determines which request types need validation based on discovered validators and DataAnnotation attributes.
    /// </summary>
    private static List<(string RequestType, string ResponseType)> DetermineValidationTargets(
        List<HandlerInfo> handlers,
        List<ValidatorInfo> validators)
    {
        var result = new List<(string RequestType, string ResponseType)>();
        var requestTypesWithValidators = new HashSet<string>(validators.Select(v => v.RequestType));

        var requestResponsePairs = handlers
            .SelectMany(h => h.RequestHandlers.Select(r => (r.RequestType, ResponseType: r.ResponseType!, r.HasDataAnnotations)))
            .Distinct()
            .ToList();

        foreach (var (requestType, responseType, hasDataAnnotations) in requestResponsePairs)
        {
            if (hasDataAnnotations || requestTypesWithValidators.Contains(requestType))
            {
                result.Add((requestType, responseType));
            }
        }

        return result;
    }

    /// <summary>
    /// Expands open generic behaviors to closed behaviors for all request/response pairs.
    /// </summary>
    private static List<ExpandedBehaviorInfo> ExpandBehaviors(
        List<BehaviorInfo> behaviors,
        List<HandlerInfo> handlers)
    {
        var expanded = new List<ExpandedBehaviorInfo>();

        var requestResponsePairs = handlers
            .SelectMany(h => h.RequestHandlers.Select(r => (r.RequestType, r.ResponseType!)))
            .Distinct()
            .ToList();

        foreach (var behavior in behaviors)
        {
            foreach (var behaviorInterface in behavior.BehaviorInterfaces)
            {
                if (behaviorInterface.IsOpenGeneric && behavior.IsOpenGeneric)
                {
                    foreach (var (requestType, responseType) in requestResponsePairs)
                    {
                        var baseTypeName = behavior.ClassName;
                        var genericMarkerIndex = baseTypeName.IndexOf('<');
                        if (genericMarkerIndex > 0)
                        {
                            baseTypeName = baseTypeName.Substring(0, genericMarkerIndex);
                        }

                        var closedBehaviorType = $"{baseTypeName}<{requestType}, {responseType}>";
                        var closedInterfaceType = $"global::MediatorLite.IPipelineBehavior<{requestType}, {responseType}>";

                        expanded.Add(new ExpandedBehaviorInfo(
                            BehaviorTypeName: closedBehaviorType,
                            RequestType: requestType,
                            ResponseType: responseType,
                            InterfaceType: closedInterfaceType,
                            Order: behavior.Order));
                    }
                }
                else if (!behaviorInterface.IsOpenGeneric)
                {
                    expanded.Add(new ExpandedBehaviorInfo(
                        BehaviorTypeName: behavior.ClassName,
                        RequestType: behaviorInterface.RequestType!,
                        ResponseType: behaviorInterface.ResponseType!,
                        InterfaceType: behaviorInterface.InterfaceType,
                        Order: behavior.Order));
                }
            }
        }

        return expanded;
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
                             /// Adds all source-generated handlers, notification handlers, behaviors, and the
                             /// source-generated mediator to the service collection.
                             /// </summary>
                             public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedHandlers(
                                 this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                             {
                                 return services;
                             }

                             /// <summary>
                             /// Adds only source-generated request handlers to the service collection.
                             /// </summary>
                             public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedRequestHandlers(
                                 this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                             {
                                 return services;
                             }

                             /// <summary>
                             /// Adds only source-generated notification handlers to the service collection.
                             /// </summary>
                             public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedNotificationHandlers(
                                 this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                             {
                                 return services;
                             }

                             /// <summary>
                             /// Adds only source-generated validators to the service collection.
                             /// </summary>
                             public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedValidators(
                                 this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                             {
                                 return services;
                             }

                             /// <summary>
                             /// Adds only source-generated pipeline behaviors to the service collection.
                             /// </summary>
                             public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedBehaviors(
                                 this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                             {
                                 return services;
                             }

                             public static int RequestHandlerCount => 0;
                             public static int NotificationHandlerCount => 0;
                             public static int BehaviorCount => 0;
                             public static int ValidatorCount => 0;
                         }
                     }
                     """;

        context.AddSource("MediatorLiteRegistration.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static void GenerateRegistrationCode(
        SourceProductionContext context,
        List<HandlerInfo> handlers,
        List<ExpandedBehaviorInfo> expandedBehaviors,
        List<ValidatorInfo> validators,
        List<(string RequestType, string ResponseType)> requestTypesWithValidation)
    {
        var requestHandlers = handlers.SelectMany(h =>
            h.RequestHandlers.Select(r => (Handler: h, Interface: r))).ToList();

        var notificationHandlers = handlers.SelectMany(h =>
            h.NotificationHandlers.Select(n => (Handler: h, Interface: n))).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace MediatorLite.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Source-generated MediatorLite registrations.");
        sb.AppendLine("    /// Provides all-in-one and granular registration methods.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class MediatorLiteRegistration");
        sb.AppendLine("    {");

        // --- All-in-one registration method ---
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Adds all source-generated handlers, notification handlers, behaviors, and the");
        sb.AppendLine("        /// source-generated mediator to the service collection.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <param name=\"services\">The service collection.</param>");
        sb.AppendLine("        /// <returns>The service collection for chaining.</returns>");
        sb.AppendLine(
            "        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedHandlers(");
        sb.AppendLine("            this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("        {");
        sb.AppendLine("            AddGeneratedRequestHandlers(services);");
        sb.AppendLine("            AddGeneratedNotificationHandlers(services);");
        sb.AppendLine("            AddGeneratedValidators(services);");
        sb.AppendLine("            AddGeneratedBehaviors(services);");
        sb.AppendLine();
        sb.AppendLine("            // Register the source-generated mediator for zero-reflection dispatch.");
        sb.AppendLine("            // Scoped: the mediator captures the resolving scope's IServiceProvider so");
        sb.AppendLine("            // handlers and behaviors resolve with correct scoped lifetimes.");
        sb.AppendLine("            services.AddScoped<global::MediatorLite.IMediator, SourceGeneratedMediator>();");
        sb.AppendLine();
        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // --- Request handler registration ---
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Adds only source-generated request handlers to the service collection.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine(
            "        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedRequestHandlers(");
        sb.AppendLine("            this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("        {");

        if (requestHandlers.Count > 0)
        {
            foreach (var (handler, iface) in requestHandlers)
            {
                sb.AppendLine($"            services.AddTransient<{iface.InterfaceType}, {handler.ClassName}>();");
            }
        }

        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // --- Notification handler registration ---
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Adds only source-generated notification handlers to the service collection.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine(
            "        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedNotificationHandlers(");
        sb.AppendLine("            this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("        {");

        if (notificationHandlers.Count > 0)
        {
            foreach (var (handler, iface) in notificationHandlers)
            {
                // Register by interface for standard DI resolution
                sb.AppendLine($"            services.AddTransient<{iface.InterfaceType}, {handler.ClassName}>();");
                // Also register concrete type for unrolled pipeline resolution
                sb.AppendLine($"            services.AddTransient<{handler.ClassName}>();");
            }
        }

        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // --- Validator registration ---
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Adds only source-generated validators to the service collection.");
        sb.AppendLine("        /// Registers custom validators and DataAnnotationsValidator for annotated request types.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine(
            "        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedValidators(");
        sb.AppendLine("            this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("        {");

        // Register discovered custom validators
        if (validators.Count > 0)
        {
            foreach (var validator in validators)
            {
                sb.AppendLine($"            services.AddTransient<{validator.InterfaceType}, {validator.ClassName}>();");
            }
        }

        // Register DataAnnotationsValidator for request types with DataAnnotation attributes
        var requestTypesWithDataAnnotations = requestHandlers
            .Where(rh => rh.Interface.HasDataAnnotations)
            .Select(rh => rh.Interface.RequestType)
            .Distinct()
            .ToList();

        if (requestTypesWithDataAnnotations.Count > 0)
        {
            foreach (var requestType in requestTypesWithDataAnnotations)
            {
                sb.AppendLine($"            services.AddTransient<global::MediatorLite.Validation.IValidator<{requestType}>, global::MediatorLite.Validation.DataAnnotationsValidator<{requestType}>>();");
            }
        }

        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // --- Behavior registration ---
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Adds only source-generated pipeline behaviors to the service collection.");
        sb.AppendLine("        /// ValidationBehavior is registered first to ensure validation runs before other behaviors.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine(
            "        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedBehaviors(");
        sb.AppendLine("            this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("        {");

        // Register ValidationBehavior FIRST for request types with validators
        // Register by concrete type so unrolled pipeline can resolve each behavior individually
        if (requestTypesWithValidation.Count > 0)
        {
            sb.AppendLine("            // Validation behaviors (registered first to ensure validation runs before other behaviors)");
            foreach (var (requestType, responseType) in requestTypesWithValidation)
            {
                sb.AppendLine($"            services.AddTransient<global::MediatorLite.Validation.ValidationBehavior<{requestType}, {responseType}>>();");
            }
            sb.AppendLine();
        }

        // Then register other (non-validation) behaviors by concrete type
        if (expandedBehaviors.Count > 0)
        {
            var nonValidationBehaviors = expandedBehaviors
                .Where(b => !b.BehaviorTypeName.StartsWith("global::MediatorLite.Validation.ValidationBehavior<"))
                .ToList();

            if (nonValidationBehaviors.Count > 0)
            {
                foreach (var behavior in nonValidationBehaviors)
                {
                    // Register by concrete type for individual resolution in unrolled pipeline
                    sb.AppendLine($"            services.AddTransient<{behavior.BehaviorTypeName}>();");
                }
            }
        }

        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // --- Diagnostic counts ---
        var totalValidatorCount = validators.Count + requestTypesWithDataAnnotations.Count;
        var nonValidationBehaviorCount = expandedBehaviors
            .Count(b => !b.BehaviorTypeName.StartsWith("global::MediatorLite.Validation.ValidationBehavior<"));

        sb.AppendLine($"        /// <summary>Number of request handlers discovered at compile time.</summary>");
        sb.AppendLine($"        public static int RequestHandlerCount => {requestHandlers.Count};");
        sb.AppendLine();
        sb.AppendLine($"        /// <summary>Number of notification handlers discovered at compile time.</summary>");
        sb.AppendLine($"        public static int NotificationHandlerCount => {notificationHandlers.Count};");
        sb.AppendLine();
        sb.AppendLine($"        /// <summary>Number of pipeline behaviors registered at compile time (including validation behaviors).</summary>");
        sb.AppendLine($"        public static int BehaviorCount => {nonValidationBehaviorCount + requestTypesWithValidation.Count};");
        sb.AppendLine();
        sb.AppendLine($"        /// <summary>Number of validators registered at compile time.</summary>");
        sb.AppendLine($"        public static int ValidatorCount => {totalValidatorCount};");

        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("MediatorLiteRegistration.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// Generates the SourceGeneratedMediator class, which implements IMediator directly:
    /// a type-pattern switch over the discovered message types dispatching to fully-typed
    /// unrolled ValueTask pipelines (no dictionaries, no boxing, no runtime wrapper).
    /// </summary>
    private static void GenerateSourceGeneratedMediator(
        SourceProductionContext context,
        List<HandlerInfo> handlers,
        List<NotificationTypeInfo> notifications,
        List<BehaviorInfo> behaviors,
        List<ExpandedBehaviorInfo> expandedBehaviors,
        AssemblyDefaults assemblyDefaults)
    {
        var requestHandlers = handlers.SelectMany(h =>
            h.RequestHandlers.Select(r => (Handler: h, Interface: r))).ToList();

        var notificationHandlers = handlers.SelectMany(h =>
            h.NotificationHandlers.Select(n => (Handler: h, Interface: n))).ToList();

        // Group behaviors by request type for unrolled pipeline generation
        var behaviorsByRequest = expandedBehaviors
            .GroupBy(b => (b.RequestType, b.ResponseType))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(b => b.Order).ToList());

        // Group notification handlers by notification type (handlers pre-sorted by order),
        // then order the groups most-derived-first for the PublishAsync type-pattern switch.
        var notificationGroups = notificationHandlers
            .GroupBy(h => h.Interface.NotificationType)
            .Select(g => (
                NotificationType: g.Key,
                Handlers: g.OrderBy(h => h.Interface.Order ?? 0).ToList()))
            .ToList();
        notificationGroups = SortMostDerivedFirst(
            notificationGroups,
            g => g.NotificationType,
            g => g.Handlers[0].Interface.BaseTypes);

        // One dispatch entry (switch arm + Send_* method) per request type. Duplicate handler
        // registrations for the same request type collapse to a single entry — resolution via
        // IRequestHandler<,> picks the last DI registration, matching previous behavior.
        var dispatchEntries = requestHandlers
            .GroupBy(rh => rh.Interface.RequestType)
            .Select(g => g.First())
            .ToList();
        dispatchEntries = SortMostDerivedFirst(
            dispatchEntries,
            rh => rh.Interface.RequestType,
            rh => rh.Interface.BaseTypes);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine();
        sb.AppendLine("namespace MediatorLite.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Source-generated <see cref=\"global::MediatorLite.IMediator\"/> implementation.");
        sb.AppendLine("    /// Dispatch is a type-pattern switch over the discovered message types with fully");
        sb.AppendLine("    /// typed, compile-time unrolled pipelines — no dictionaries, no boxing, and no");
        sb.AppendLine("    /// async state machine on synchronous zero-behavior paths.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public sealed class SourceGeneratedMediator : global::MediatorLite.IMediator");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly IServiceProvider _sp;");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>Creates the mediator bound to the resolving scope's service provider.</summary>");
        sb.AppendLine("        public SourceGeneratedMediator(IServiceProvider serviceProvider)");
        sb.AppendLine("        {");
        sb.AppendLine("            _sp = serviceProvider;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // === TYPED REQUEST DISPATCH ===
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public ValueTask<TResponse> SendAsync<TResponse>(");
        sb.AppendLine("            global::MediatorLite.IRequest<TResponse> request,");
        sb.AppendLine("            CancellationToken cancellationToken = default)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (request)");
        sb.AppendLine("            {");
        foreach (var (handler, iface) in dispatchEntries)
        {
            var safeName = GetSafeTypeName(iface.RequestType);
            sb.AppendLine($"                case {iface.RequestType} r_{safeName}:");
            sb.AppendLine("                {");
            sb.AppendLine($"                    var vt = Send_{safeName}(r_{safeName}, cancellationToken);");
            sb.AppendLine($"                    if (typeof(TResponse) == typeof({iface.ResponseType}))");
            sb.AppendLine("                    {");
            sb.AppendLine("                        // SAFETY: guarded by the exact type-equality check above, this is an");
            sb.AppendLine("                        // identity reinterpret (ValueTask<T> as ValueTask<T>). The guard is");
            sb.AppendLine("                        // JIT-folded to a constant, so the cast itself is free.");
            sb.AppendLine($"                        return Unsafe.As<ValueTask<{iface.ResponseType}>, ValueTask<TResponse>>(ref vt);");
            sb.AppendLine("                    }");
            sb.AppendLine($"                    return SlowCast<{iface.ResponseType}, TResponse>(vt);");
            sb.AppendLine("                }");
        }
        sb.AppendLine("                case null:");
        sb.AppendLine("                    throw new ArgumentNullException(nameof(request));");
        sb.AppendLine("                default:");
        sb.AppendLine("                    throw new InvalidOperationException(");
        sb.AppendLine("                        $\"No handler registered for request type {request.GetType().FullName}. \" +");
        sb.AppendLine("                        \"Ensure a handler implementing IRequestHandler<TRequest, TResponse> exists \" +");
        sb.AppendLine("                        \"in a project the source generator runs on, and AddGeneratedHandlers() is called.\");");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        // Only reachable when the caller dispatched through a covariant IRequest<TBase>");
        sb.AppendLine("        // reference (TResponse is then a reference type): plain reference cast, no boxing.");
        sb.AppendLine("        private static async ValueTask<TResponse> SlowCast<TConcrete, TResponse>(ValueTask<TConcrete> task)");
        sb.AppendLine("            => (TResponse)(object)(await task.ConfigureAwait(false))!;");
        sb.AppendLine();

        // === TYPED NOTIFICATION DISPATCH ===
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public ValueTask PublishAsync<TNotification>(");
        sb.AppendLine("            TNotification notification,");
        sb.AppendLine("            CancellationToken cancellationToken = default)");
        sb.AppendLine("            where TNotification : global::MediatorLite.INotification");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (notification)");
        sb.AppendLine("            {");
        foreach (var group in notificationGroups)
        {
            var safeName = GetSafeTypeName(group.NotificationType);
            sb.AppendLine($"                case {group.NotificationType} n_{safeName}:");
            sb.AppendLine($"                    return Publish_{safeName}(n_{safeName}, cancellationToken);");
        }
        sb.AppendLine("                case null:");
        sb.AppendLine("                    throw new ArgumentNullException(nameof(notification));");
        sb.AppendLine("                default:");
        sb.AppendLine("                    return default; // No handlers registered for this notification type.");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        // === UNROLLED REQUEST PIPELINES ===
        sb.AppendLine("        // ═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("        // UNROLLED REQUEST PIPELINES — Fully typed, zero-overhead dispatch");
        sb.AppendLine("        // ═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();

        foreach (var (handler, iface) in dispatchEntries)
        {
            var safeName = GetSafeTypeName(iface.RequestType);
            var requestType = iface.RequestType;
            var responseType = iface.ResponseType!;

            // Get behaviors for this request type, sorted by order
            var key = (requestType, responseType);
            var behaviorsForRequest = behaviorsByRequest.ContainsKey(key)
                ? behaviorsByRequest[key]
                : new List<ExpandedBehaviorInfo>();

            GenerateUnrolledPipeline(
                sb,
                safeName,
                requestType,
                responseType,
                behaviorsForRequest,
                loggingEnabled: !assemblyDefaults.LoggingDisabled,
                tracingEnabled: !assemblyDefaults.TracingDisabled);
        }

        // === UNROLLED NOTIFICATION PUBLISHERS ===
        sb.AppendLine("        // ═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("        // UNROLLED NOTIFICATION PUBLISHERS — Handlers pre-sorted by order");
        sb.AppendLine("        // ═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();

        var notificationsByType = notifications
            .GroupBy(n => n.TypeName)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var group in notificationGroups)
        {
            var notificationType = group.NotificationType;
            var safeName = GetSafeTypeName(notificationType);
            var handlersForNotification = group.Handlers;

            notificationsByType.TryGetValue(notificationType, out var perTypeOptions);
            var (executionStrategy, errorStrategy) = ResolveStrategies(perTypeOptions, assemblyDefaults);

            GenerateUnrolledNotificationPublisher(
                sb,
                safeName,
                notificationType,
                handlersForNotification,
                executionStrategy,
                errorStrategy,
                loggingEnabled: !assemblyDefaults.LoggingDisabled,
                tracingEnabled: !assemblyDefaults.TracingDisabled);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("SourceGeneratedMediator.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// Generates a fully-typed unrolled send method for a specific request type, returning
    /// <c>ValueTask&lt;TResponse&gt;</c> end-to-end (no boxing, no type-erased delegates).
    /// When <paramref name="loggingEnabled"/> or <paramref name="tracingEnabled"/> is true,
    /// the body is an async method wrapped in try/catch with inline logging/tracing. When both
    /// are false, the pipeline ValueTask is returned directly with no async state machine in
    /// the mediator — for zero-behavior requests this makes synchronous handler completions
    /// fully allocation-free.
    /// </summary>
    private static void GenerateUnrolledPipeline(
        StringBuilder sb,
        string safeName,
        string requestType,
        string responseType,
        List<ExpandedBehaviorInfo> behaviors,
        bool loggingEnabled,
        bool tracingEnabled)
    {
        // Display names for log messages / tag values. `requestType` is already fully
        // qualified (starts with "global::"); strip that prefix for the tag value so the
        // emitted literal matches typeof(T).FullName-like output, and compute the simple
        // name by taking the substring after the last '.'.
        var fullRequest = StripGlobalPrefix(requestType);
        var fullResponse = StripGlobalPrefix(responseType);
        var simpleRequest = requestType.Substring(requestType.LastIndexOf('.') + 1);

        // The nested behavior/handler invocation chain, built outside-in. With no behaviors
        // this is just the handler call.
        string BuildPipelineExpression()
        {
            var expr = new StringBuilder();
            for (int i = 0; i < behaviors.Count; i++)
            {
                expr.Append($"b{i + 1}.HandleAsync(request, () => ");
            }
            expr.Append("handler.HandleAsync(request, ct)");
            for (int i = behaviors.Count - 1; i >= 0; i--)
            {
                expr.Append(", ct)");
            }
            return expr.ToString();
        }

        void EmitResolutions(string indent)
        {
            for (int i = 0; i < behaviors.Count; i++)
            {
                sb.AppendLine($"{indent}var b{i + 1} = _sp.GetRequiredService<{behaviors[i].BehaviorTypeName}>();");
            }
            sb.AppendLine($"{indent}var handler = _sp.GetRequiredService<global::MediatorLite.IRequestHandler<{requestType}, {responseType}>>();");
        }

        bool needsDiagnostics = loggingEnabled || tracingEnabled;

        if (!needsDiagnostics)
        {
            // Fully-disabled fast path — return the pipeline ValueTask directly; no async
            // state machine, no try/catch, no diagnostic locals.
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        private ValueTask<{responseType}> Send_{safeName}({requestType} request, CancellationToken ct)");
            sb.AppendLine("        {");
            EmitResolutions("            ");
            sb.AppendLine($"            return {BuildPipelineExpression()};");
            sb.AppendLine("        }");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"        private async ValueTask<{responseType}> Send_{safeName}({requestType} request, CancellationToken ct)");
        sb.AppendLine("        {");

        if (loggingEnabled)
        {
            sb.AppendLine("            var __logger = _sp.GetRequiredService<global::Microsoft.Extensions.Logging.ILogger<global::MediatorLite.IMediator>>();");
            sb.AppendLine($"            __logger.LogDebug(\"Sending request {{RequestType}}\", \"{simpleRequest}\");");
        }
        if (tracingEnabled)
        {
            sb.AppendLine("            using var __activity = global::MediatorLite.Diagnostics.MediatorActivitySource.Source.StartActivity(");
            sb.AppendLine($"                \"{ActivityNameSendRequest} {simpleRequest}\",");
            sb.AppendLine("                global::System.Diagnostics.ActivityKind.Internal);");
            sb.AppendLine($"            __activity?.SetTag(global::MediatorLite.Diagnostics.MediatorActivitySource.Tags.RequestType, \"{fullRequest}\");");
            sb.AppendLine($"            __activity?.SetTag(global::MediatorLite.Diagnostics.MediatorActivitySource.Tags.ResponseType, \"{fullResponse}\");");
        }
        sb.AppendLine();
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        EmitResolutions("                ");
        sb.AppendLine($"                var result = await {BuildPipelineExpression()}.ConfigureAwait(false);");
        if (loggingEnabled)
        {
            sb.AppendLine($"                __logger.LogDebug(\"Request {{RequestType}} handled successfully\", \"{simpleRequest}\");");
        }
        sb.AppendLine("                return result;");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (global::System.Exception __ex)");
        sb.AppendLine("            {");
        if (tracingEnabled)
        {
            sb.AppendLine("                __activity?.SetTag(global::MediatorLite.Diagnostics.MediatorActivitySource.Tags.Error, true);");
            sb.AppendLine("                __activity?.SetTag(global::MediatorLite.Diagnostics.MediatorActivitySource.Tags.ErrorMessage, __ex.Message);");
        }
        if (loggingEnabled)
        {
            sb.AppendLine($"                __logger.LogError(__ex, \"Error handling request {{RequestType}}\", \"{simpleRequest}\");");
        }
        sb.AppendLine("                throw;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    /// <summary>
    /// Generates an unrolled notification publisher method.
    /// Strategies are fully resolved at compile time by <see cref="ResolveStrategies"/>,
    /// so the emitted body has exactly one code path with no runtime branching on strategy.
    /// When <paramref name="loggingEnabled"/> or <paramref name="tracingEnabled"/> is true,
    /// the strategy body is wrapped in try/catch with inline diagnostics.
    /// </summary>
    /// <remarks>
    /// NOTE: <see cref="GenerateSequentialNotificationExecution"/>, <see cref="GenerateParallelNotificationExecution"/>
    /// and <see cref="GenerateStopOnFirstNotificationExecution"/> hardcode 12-space indentation.
    /// When wrapped in try/catch the emitted body is slightly under-indented (cosmetic only — C# is
    /// whitespace-insensitive). This is accepted rather than plumbing an indent parameter through
    /// all three helpers.
    /// </remarks>
    private static void GenerateUnrolledNotificationPublisher(
        StringBuilder sb,
        string safeName,
        string notificationType,
        List<(HandlerInfo Handler, NotificationHandlerInterfaceInfo Interface)> handlers,
        int executionStrategy,
        int errorStrategy,
        bool loggingEnabled,
        bool tracingEnabled)
    {
        var fullNotification = StripGlobalPrefix(notificationType);
        var simpleNotification = notificationType.Substring(notificationType.LastIndexOf('.') + 1);

        void EmitStrategyBody()
        {
            if (handlers.Count == 0)
            {
                sb.AppendLine("            // No handlers registered");
                sb.AppendLine("            await Task.CompletedTask;");
            }
            else if (executionStrategy == 1) // Parallel
            {
                GenerateParallelNotificationExecution(sb, notificationType, handlers, errorStrategy);
            }
            else if (executionStrategy == 2) // StopOnFirst
            {
                GenerateStopOnFirstNotificationExecution(sb, notificationType, handlers, errorStrategy);
            }
            else // Sequential (default)
            {
                GenerateSequentialNotificationExecution(sb, notificationType, handlers, errorStrategy);
            }
        }

        bool needsDiagnostics = loggingEnabled || tracingEnabled;

        // Single-handler fast path. With immediate error propagation (StopOnFirstError) every
        // execution strategy degenerates to one invocation, so return the handler's ValueTask
        // directly with no async state machine in the mediator.
        if (!needsDiagnostics && handlers.Count == 1 && errorStrategy == 0)
        {
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        private ValueTask Publish_{safeName}({notificationType} notification, CancellationToken ct)");
            sb.AppendLine("        {");
            sb.AppendLine("            ct.ThrowIfCancellationRequested();");
            sb.AppendLine($"            var h1 = _sp.GetRequiredService<{handlers[0].Handler.ClassName}>();");
            sb.AppendLine("            return h1.HandleAsync(notification, ct);");
            sb.AppendLine("        }");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"        private async ValueTask Publish_{safeName}({notificationType} notification, CancellationToken ct)");
        sb.AppendLine("        {");

        if (needsDiagnostics)
        {
            if (loggingEnabled)
            {
                sb.AppendLine("            var __logger = _sp.GetRequiredService<global::Microsoft.Extensions.Logging.ILogger<global::MediatorLite.IMediator>>();");
                sb.AppendLine($"            __logger.LogDebug(\"Publishing notification {{NotificationType}}\", \"{simpleNotification}\");");
            }
            if (tracingEnabled)
            {
                sb.AppendLine("            using var __activity = global::MediatorLite.Diagnostics.MediatorActivitySource.Source.StartActivity(");
                sb.AppendLine($"                \"{ActivityNamePublishNotification} {simpleNotification}\",");
                sb.AppendLine("                global::System.Diagnostics.ActivityKind.Internal);");
                sb.AppendLine($"            __activity?.SetTag(global::MediatorLite.Diagnostics.MediatorActivitySource.Tags.NotificationType, \"{fullNotification}\");");
            }
            sb.AppendLine();
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            // Strategy helpers hardcode 12-space indent — accepted cosmetic under-indentation here.
            EmitStrategyBody();
            if (loggingEnabled)
            {
                sb.AppendLine($"                __logger.LogDebug(\"Notification {{NotificationType}} published successfully\", \"{simpleNotification}\");");
            }
            sb.AppendLine("            }");
            sb.AppendLine("            catch (global::System.Exception __ex)");
            sb.AppendLine("            {");
            if (tracingEnabled)
            {
                sb.AppendLine("                __activity?.SetTag(global::MediatorLite.Diagnostics.MediatorActivitySource.Tags.Error, true);");
                sb.AppendLine("                __activity?.SetTag(global::MediatorLite.Diagnostics.MediatorActivitySource.Tags.ErrorMessage, __ex.Message);");
            }
            if (loggingEnabled)
            {
                sb.AppendLine($"                __logger.LogError(__ex, \"Error publishing notification {{NotificationType}}\", \"{simpleNotification}\");");
            }
            sb.AppendLine("                throw;");
            sb.AppendLine("            }");
        }
        else
        {
            // Fast path: emit strategy body directly
            EmitStrategyBody();
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private static void GenerateSequentialNotificationExecution(
        StringBuilder sb,
        string notificationType,
        List<(HandlerInfo Handler, NotificationHandlerInterfaceInfo Interface)> handlers,
        int errorStrategy)
    {
        // Resolve handlers
        for (int i = 0; i < handlers.Count; i++)
        {
            var handler = handlers[i];
            sb.AppendLine($"            var h{i + 1} = _sp.GetRequiredService<{handler.Handler.ClassName}>();");
        }
        sb.AppendLine();

        if (errorStrategy == 1) // ContinueAndAggregate
        {
            sb.AppendLine("            List<Exception>? exceptions = null;");
            sb.AppendLine();

            for (int i = 0; i < handlers.Count; i++)
            {
                sb.AppendLine("            try");
                sb.AppendLine("            {");
                sb.AppendLine("                ct.ThrowIfCancellationRequested();");
                sb.AppendLine($"                await h{i + 1}.HandleAsync(notification, ct).ConfigureAwait(false);");
                sb.AppendLine("            }");
                sb.AppendLine("            catch (OperationCanceledException) { throw; }");
                sb.AppendLine("            catch (Exception ex)");
                sb.AppendLine("            {");
                sb.AppendLine("                (exceptions ??= new List<Exception>()).Add(ex);");
                sb.AppendLine("            }");
                sb.AppendLine();
            }

            sb.AppendLine("            if (exceptions is { Count: > 0 })");
            sb.AppendLine("            {");
            sb.AppendLine($"                throw new AggregateException(\"One or more notification handlers threw exceptions.\", exceptions);");
            sb.AppendLine("            }");
        }
        else // StopOnFirstError (default)
        {
            for (int i = 0; i < handlers.Count; i++)
            {
                sb.AppendLine("            ct.ThrowIfCancellationRequested();");
                sb.AppendLine($"            await h{i + 1}.HandleAsync(notification, ct).ConfigureAwait(false);");
            }
        }
    }

    private static void GenerateParallelNotificationExecution(
        StringBuilder sb,
        string notificationType,
        List<(HandlerInfo Handler, NotificationHandlerInterfaceInfo Interface)> handlers,
        int errorStrategy)
    {
        // Resolve handlers
        for (int i = 0; i < handlers.Count; i++)
        {
            var handler = handlers[i];
            sb.AppendLine($"            var h{i + 1} = _sp.GetRequiredService<{handler.Handler.ClassName}>();");
        }
        sb.AppendLine();

        // Start every handler before the first await so they run concurrently, capturing
        // synchronous throws as faulted ValueTasks. No task array and no AsTask() conversions —
        // synchronously completing handlers never allocate a Task at all.
        for (int i = 0; i < handlers.Count; i++)
        {
            sb.AppendLine($"            ValueTask vt{i + 1};");
            sb.AppendLine($"            try {{ vt{i + 1} = h{i + 1}.HandleAsync(notification, ct); }}");
            sb.AppendLine($"            catch (global::System.Exception __ex{i}) {{ vt{i + 1} = ValueTask.FromException(__ex{i}); }}");
        }
        sb.AppendLine();

        if (errorStrategy == 1) // ContinueAndAggregate
        {
            sb.AppendLine("            List<Exception>? exceptions = null;");
            for (int i = 0; i < handlers.Count; i++)
            {
                sb.AppendLine($"            try {{ await vt{i + 1}.ConfigureAwait(false); }}");
                sb.AppendLine("            catch (Exception ex) { (exceptions ??= new List<Exception>()).Add(ex); }");
            }
            sb.AppendLine();
            sb.AppendLine("            if (exceptions is not null)");
            sb.AppendLine("            {");
            sb.AppendLine("                // Prioritize cancellation - rethrow OperationCanceledException directly");
            sb.AppendLine("                ct.ThrowIfCancellationRequested();");
            sb.AppendLine("                // For handler failures, throw an AggregateException with all exceptions");
            sb.AppendLine("                throw new AggregateException(exceptions);");
            sb.AppendLine("            }");
        }
        else // StopOnFirstError - every task is observed; the first failure (in handler order) is rethrown
        {
            sb.AppendLine("            Exception? firstException = null;");
            for (int i = 0; i < handlers.Count; i++)
            {
                sb.AppendLine($"            try {{ await vt{i + 1}.ConfigureAwait(false); }}");
                sb.AppendLine("            catch (Exception ex) { firstException ??= ex; }");
            }
            sb.AppendLine();
            sb.AppendLine("            if (firstException is not null)");
            sb.AppendLine("            {");
            sb.AppendLine("                global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstException).Throw();");
            sb.AppendLine("            }");
        }
    }

    private static void GenerateStopOnFirstNotificationExecution(
        StringBuilder sb,
        string notificationType,
        List<(HandlerInfo Handler, NotificationHandlerInterfaceInfo Interface)> handlers,
        int errorStrategy)
    {
        if (errorStrategy == 0) // StopOnFirstError
        {
            // With immediate error propagation, StopOnFirst always runs exactly the first
            // handler: it either succeeds (stop) or throws (stop). Later handlers are
            // unreachable, so only the first is resolved.
            sb.AppendLine("            ct.ThrowIfCancellationRequested();");
            sb.AppendLine($"            var h1 = _sp.GetRequiredService<{handlers[0].Handler.ClassName}>();");
            sb.AppendLine("            await h1.HandleAsync(notification, ct).ConfigureAwait(false);");
            return;
        }

        // Resolve handlers
        for (int i = 0; i < handlers.Count; i++)
        {
            var handler = handlers[i];
            sb.AppendLine($"            var h{i + 1} = _sp.GetRequiredService<{handler.Handler.ClassName}>();");
        }
        sb.AppendLine();

        if (errorStrategy == 1) // ContinueAndAggregate
        {
            sb.AppendLine("            List<Exception>? exceptions = null;");
            sb.AppendLine();

            for (int i = 0; i < handlers.Count; i++)
            {
                sb.AppendLine("            try");
                sb.AppendLine("            {");
                sb.AppendLine("                ct.ThrowIfCancellationRequested();");
                sb.AppendLine($"                await h{i + 1}.HandleAsync(notification, ct).ConfigureAwait(false);");
                sb.AppendLine("                return; // Success — stop here");
                sb.AppendLine("            }");
                sb.AppendLine("            catch (OperationCanceledException) { throw; }");
                sb.AppendLine("            catch (Exception ex)");
                sb.AppendLine("            {");
                sb.AppendLine("                (exceptions ??= new List<Exception>()).Add(ex);");
                sb.AppendLine("            }");
                sb.AppendLine();
            }

            sb.AppendLine("            if (exceptions is { Count: > 0 })");
            sb.AppendLine("            {");
            sb.AppendLine($"                throw new AggregateException(\"All notification handlers threw exceptions.\", exceptions);");
            sb.AppendLine("            }");
        }
    }

    /// <summary>
    /// Converts a fully qualified type name to a safe C# identifier.
    /// </summary>
    private static string GetSafeTypeName(string typeName)
    {
        return typeName
            .Replace("global::", "")
            .Replace(".", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace(" ", "");
    }

    /// <summary>
    /// Strips a leading "global::" prefix for use in emitted string literals (tag values,
    /// log message arguments). The returned value is suitable to embed inside a C# string literal.
    /// </summary>
    private static string StripGlobalPrefix(string typeName)
    {
        const string prefix = "global::";
        return typeName.StartsWith(prefix) ? typeName.Substring(prefix.Length) : typeName;
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
    string? ResponseType,
    bool HasDataAnnotations = false,
    List<string>? BaseTypes = null);

internal sealed record NotificationHandlerInterfaceInfo(
    string InterfaceType,
    string NotificationType,
    int? Order,
    List<string>? BaseTypes = null);

internal sealed record NotificationTypeInfo(
    string TypeName,
    int? ExecutionStrategy,
    int? ErrorStrategy);

internal readonly record struct AssemblyDefaults(
    int? ExecutionStrategy,
    int? ErrorStrategy,
    bool LoggingDisabled,
    bool TracingDisabled);

internal sealed record BehaviorInfo(
    string ClassName,
    string Namespace,
    List<BehaviorInterfaceInfo> BehaviorInterfaces,
    bool IsOpenGeneric,
    int Order = 0);

internal sealed record BehaviorInterfaceInfo(
    string InterfaceType,
    string? RequestType,
    string? ResponseType,
    bool IsOpenGeneric);

internal sealed record ExpandedBehaviorInfo(
    string BehaviorTypeName,
    string RequestType,
    string ResponseType,
    string InterfaceType,
    int Order = 0);

internal sealed record ValidatorInfo(
    string ClassName,
    string Namespace,
    string InterfaceType,
    string RequestType);
