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

        // Combine with compilation
        var compilationAndData = context.CompilationProvider
            .Combine(handlerDeclarations.Collect())
            .Combine(notificationDeclarations.Collect())
            .Combine(behaviorDeclarations.Collect())
            .Combine(validatorDeclarations.Collect());

        // Generate the output
        context.RegisterSourceOutput(compilationAndData, static (spc, source) =>
        {
            var ((((compilation, handlers), notifications), behaviors), validators) = source;
            Execute(spc, compilation, handlers!, notifications!, behaviors!, validators!);
        });
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

        var optionsAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "NotificationOptionsAttribute");

        if (optionsAttr == null)
            return null;

        int executionStrategy = 0;
        int errorStrategy = 1;
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

        return new BehaviorInfo(
            ClassName: classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace: classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
            BehaviorInterfaces: behaviorInterfaces,
            IsOpenGeneric: isOpenGeneric);
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
                        HasDataAnnotations: hasDataAnnotations));
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
        ImmutableArray<NotificationTypeInfo?> notifications,
        ImmutableArray<BehaviorInfo?> behaviors,
        ImmutableArray<ValidatorInfo?> validators)
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
        GenerateSourceGeneratedMediator(context, validHandlers, validNotifications, validBehaviors, expandedBehaviors);
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
                            InterfaceType: closedInterfaceType));
                    }
                }
                else if (!behaviorInterface.IsOpenGeneric)
                {
                    expanded.Add(new ExpandedBehaviorInfo(
                        BehaviorTypeName: behavior.ClassName,
                        RequestType: behaviorInterface.RequestType!,
                        ResponseType: behaviorInterface.ResponseType!,
                        InterfaceType: behaviorInterface.InterfaceType));
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
        sb.AppendLine("            // Register the source-generated mediator for zero-reflection dispatch");
        sb.AppendLine("            services.AddSingleton<global::MediatorLite.ISourceGeneratedMediator, SourceGeneratedMediator>();");
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
                sb.AppendLine($"            services.AddTransient<{iface.InterfaceType}, {handler.ClassName}>();");
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
        if (requestTypesWithValidation.Count > 0)
        {
            sb.AppendLine("            // Validation behaviors (registered first to ensure validation runs before other behaviors)");
            foreach (var (requestType, responseType) in requestTypesWithValidation)
            {
                sb.AppendLine($"            services.AddTransient<global::MediatorLite.IPipelineBehavior<{requestType}, {responseType}>, global::MediatorLite.Validation.ValidationBehavior<{requestType}, {responseType}>>();");
            }
            sb.AppendLine();
        }

        // Then register other (non-validation) behaviors
        if (expandedBehaviors.Count > 0)
        {
            var nonValidationBehaviors = expandedBehaviors
                .Where(b => !b.BehaviorTypeName.StartsWith("global::MediatorLite.Validation.ValidationBehavior<"))
                .ToList();

            if (nonValidationBehaviors.Count > 0)
            {
                foreach (var behavior in nonValidationBehaviors)
                {
                    sb.AppendLine($"            services.AddTransient<{behavior.InterfaceType}, {behavior.BehaviorTypeName}>();");
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
    /// Generates the ISourceGeneratedMediator implementation with pattern matching dispatch.
    /// </summary>
    private static void GenerateSourceGeneratedMediator(
        SourceProductionContext context,
        List<HandlerInfo> handlers,
        List<NotificationTypeInfo> notifications,
        List<BehaviorInfo> behaviors,
        List<ExpandedBehaviorInfo> expandedBehaviors)
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
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace MediatorLite.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Source-generated mediator implementation that provides zero-reflection dispatch");
        sb.AppendLine("    /// for compile-time discovered handlers, behaviors, and notifications.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public sealed class SourceGeneratedMediator : global::MediatorLite.ISourceGeneratedMediator");
        sb.AppendLine("    {");

        // Generate handler order map
        var notificationHandlersWithOrder = notificationHandlers.Where(h => h.Interface.Order.HasValue).ToList();
        if (notificationHandlersWithOrder.Count > 0)
        {
            sb.AppendLine("        private static readonly Dictionary<string, int> _handlerOrderMap = new(StringComparer.Ordinal)");
            sb.AppendLine("        {");
            foreach (var (handler, iface) in notificationHandlersWithOrder)
            {
                var handlerName = handler.ClassName.Replace("global::", "");
                sb.AppendLine($"            {{ \"{handlerName}\", {iface.Order!.Value} }},");
            }
            sb.AppendLine("        };");
            sb.AppendLine();
        }

        // Generate notification options map
        if (notifications.Count > 0)
        {
            sb.AppendLine("        private static readonly Dictionary<string, (global::MediatorLite.NotificationExecutionStrategy, global::MediatorLite.NotificationErrorStrategy)> _notificationOptionsMap = new(StringComparer.Ordinal)");
            sb.AppendLine("        {");
            foreach (var notification in notifications)
            {
                var notificationType = notification.TypeName.Replace("global::", "");
                sb.AppendLine($"            {{ \"{notificationType}\", ((global::MediatorLite.NotificationExecutionStrategy){notification.ExecutionStrategy}, (global::MediatorLite.NotificationErrorStrategy){notification.ErrorStrategy}) }},");
            }
            sb.AppendLine("        };");
            sb.AppendLine();
        }
        sb.AppendLine();

        // --- TrySendAsync ---
        GenerateTrySendAsync(sb, requestHandlers);

        // --- TryInvokeHandlerAsync ---
        GenerateTryInvokeHandlerAsync(sb, requestHandlers);

        // --- TryGetHandlerOrder ---
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public int? TryGetHandlerOrder(Type handlerType)");
        sb.AppendLine("        {");
        if (notificationHandlersWithOrder.Count > 0)
        {
            sb.AppendLine("            return _handlerOrderMap.TryGetValue(handlerType.FullName ?? string.Empty, out var order) ? order : null;");
        }
        else
        {
            sb.AppendLine("            return null;");
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        // --- TryGetNotificationOptions ---
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public (global::MediatorLite.NotificationExecutionStrategy ExecutionStrategy, global::MediatorLite.NotificationErrorStrategy ErrorStrategy)? TryGetNotificationOptions(Type notificationType)");
        sb.AppendLine("        {");
        if (notifications.Count > 0)
        {
            sb.AppendLine("            return _notificationOptionsMap.TryGetValue(notificationType.FullName ?? string.Empty, out var options) ? options : null;");
        }
        else
        {
            sb.AppendLine("            return null;");
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        // --- TryGetCachedHandlers ---
        GenerateTryGetCachedHandlers(sb, notificationHandlers);

        // --- TryResolveBehaviors ---
        GenerateTryResolveBehaviors(sb, requestHandlers);

        // --- InvokeHandler ---
        GenerateInvokeHandler(sb, requestHandlers);

        // --- InvokeBehavior ---
        GenerateInvokeBehavior(sb, expandedBehaviors);

        // DispatchAs helper
        sb.AppendLine("        private static async ValueTask<TResponse> DispatchAs<TResponse, TActual>(ValueTask<TActual> task)");
        sb.AppendLine("        {");
        sb.AppendLine("            var result = await task.ConfigureAwait(false);");
        sb.AppendLine("            return (TResponse)(object)result!;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("SourceGeneratedMediator.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void GenerateTrySendAsync(StringBuilder sb,
        List<(HandlerInfo Handler, HandlerInterfaceInfo Interface)> requestHandlers)
    {
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
                sb.AppendLine($"                {iface.RequestType} r => DispatchAs<TResponse, {iface.ResponseType}>(");
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
    }

    private static void GenerateTryInvokeHandlerAsync(StringBuilder sb,
        List<(HandlerInfo Handler, HandlerInterfaceInfo Interface)> requestHandlers)
    {
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
                sb.AppendLine($"                {iface.RequestType} r => DispatchAs<TResponse, {iface.ResponseType}>(");
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
    }

    private static void GenerateTryGetCachedHandlers(StringBuilder sb,
        List<(HandlerInfo Handler, NotificationHandlerInterfaceInfo Interface)> notificationHandlers)
    {
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public IReadOnlyList<global::MediatorLite.INotificationHandler<TNotification>>? TryGetCachedHandlers<TNotification>(");
        sb.AppendLine("            IServiceProvider serviceProvider)");
        sb.AppendLine("            where TNotification : global::MediatorLite.INotification");
        sb.AppendLine("        {");

        var notificationTypesWithHandlers = notificationHandlers
            .GroupBy(h => h.Interface.NotificationType)
            .Where(g => g.Count() > 0)
            .ToList();

        if (notificationTypesWithHandlers.Count > 0)
        {
            sb.AppendLine("            var notificationType = typeof(TNotification);");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                return notificationType switch");
            sb.AppendLine("                {");

            foreach (var group in notificationTypesWithHandlers)
            {
                var noticeType = group.Key;
                var handlersList = group.Select(h => h.Handler).Distinct().ToList();
                sb.AppendLine($"                    Type t when t == typeof({noticeType}) => new List<global::MediatorLite.INotificationHandler<TNotification>>");
                sb.AppendLine("                    {");
                foreach (var handler in handlersList)
                {
                    sb.AppendLine($"                        (global::MediatorLite.INotificationHandler<TNotification>)(object)serviceProvider.GetRequiredService<{handler.ClassName}>(),");
                }
                sb.AppendLine("                    }.AsReadOnly(),");
            }

            sb.AppendLine("                    _ => null,");
            sb.AppendLine("                };");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (InvalidOperationException)");
            sb.AppendLine("            {");
            sb.AppendLine("                return null;");
            sb.AppendLine("            }");
        }
        else
        {
            sb.AppendLine("            return null;");
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private static void GenerateTryResolveBehaviors(StringBuilder sb,
        List<(HandlerInfo Handler, HandlerInterfaceInfo Interface)> requestHandlers)
    {
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public List<object>? TryResolveBehaviors(");
        sb.AppendLine("            IServiceProvider serviceProvider,");
        sb.AppendLine("            Type requestType,");
        sb.AppendLine("            Type responseType)");
        sb.AppendLine("        {");

        if (requestHandlers.Count > 0)
        {
            sb.AppendLine("            // Use typed resolution for known request/response pairs to avoid MakeGenericType");
            sb.AppendLine("            return (requestType, responseType) switch");
            sb.AppendLine("            {");

            foreach (var (_, iface) in requestHandlers)
            {
                sb.AppendLine($"                (Type t, Type r) when t == typeof({iface.RequestType}) && r == typeof({iface.ResponseType}) =>");
                sb.AppendLine($"                    ResolveBehaviorsFor_{GetSafeTypeName(iface.RequestType)}(serviceProvider),");
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

        // Generate typed behavior resolution helper methods for each request type
        if (requestHandlers.Count > 0)
        {
            foreach (var (_, iface) in requestHandlers)
            {
                var safeName = GetSafeTypeName(iface.RequestType);
                sb.AppendLine($"        private static List<object> ResolveBehaviorsFor_{safeName}(IServiceProvider serviceProvider)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var behaviors = new List<object>();");
                sb.AppendLine($"            foreach (var behavior in serviceProvider.GetServices<global::MediatorLite.IPipelineBehavior<{iface.RequestType}, {iface.ResponseType}>>())");
                sb.AppendLine("            {");
                sb.AppendLine("                if (behavior != null)");
                sb.AppendLine("                    behaviors.Add(behavior);");
                sb.AppendLine("            }");
                sb.AppendLine("            return behaviors;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
        }
    }

    private static void GenerateInvokeHandler(StringBuilder sb,
        List<(HandlerInfo Handler, HandlerInterfaceInfo Interface)> requestHandlers)
    {
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public ValueTask<TResponse> InvokeHandler<TResponse>(");
        sb.AppendLine("            Type requestType,");
        sb.AppendLine("            object handler,");
        sb.AppendLine("            object request,");
        sb.AppendLine("            CancellationToken cancellationToken)");
        sb.AppendLine("        {");

        if (requestHandlers.Count > 0)
        {
            sb.AppendLine("            return (requestType, typeof(TResponse)) switch");
            sb.AppendLine("            {");
            foreach (var (_, iface) in requestHandlers)
            {
                sb.AppendLine($"                (Type t, Type r) when t == typeof({iface.RequestType}) && r == typeof({iface.ResponseType}) =>");
                sb.AppendLine($"                    (ValueTask<TResponse>)(object)((({iface.InterfaceType})handler).HandleAsync(({iface.RequestType})request, cancellationToken)),");
            }
            sb.AppendLine();
            sb.AppendLine("                _ => throw new InvalidOperationException(");
            sb.AppendLine("                    $\"No source-generated handler for request type {requestType.FullName}.\")");
            sb.AppendLine("            };");
        }
        else
        {
            sb.AppendLine("            throw new InvalidOperationException(\"No handlers discovered at compile time.\");");
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private static void GenerateInvokeBehavior(StringBuilder sb,
        List<ExpandedBehaviorInfo> expandedBehaviors)
    {
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public ValueTask<TResponse> InvokeBehavior<TResponse>(");
        sb.AppendLine("            Type requestType,");
        sb.AppendLine("            Type behaviorType,");
        sb.AppendLine("            object behavior,");
        sb.AppendLine("            object request,");
        sb.AppendLine("            global::MediatorLite.RequestHandlerDelegate<TResponse> next,");
        sb.AppendLine("            CancellationToken cancellationToken)");
        sb.AppendLine("        {");

        if (expandedBehaviors.Count > 0)
        {
            sb.AppendLine("            return (requestType, typeof(TResponse), behaviorType) switch");
            sb.AppendLine("            {");

            foreach (var behav in expandedBehaviors)
            {
                sb.AppendLine($"                (Type t, Type r, Type b)");
                sb.AppendLine($"                    when t == typeof({behav.RequestType})");
                sb.AppendLine($"                    && r == typeof({behav.ResponseType})");
                sb.AppendLine($"                    && b == typeof({behav.BehaviorTypeName}) =>");
                sb.AppendLine($"                    (ValueTask<TResponse>)(object)((({behav.InterfaceType})behavior).HandleAsync(");
                sb.AppendLine($"                        ({behav.RequestType})request,");
                sb.AppendLine($"                        (global::MediatorLite.RequestHandlerDelegate<{behav.ResponseType}>)(object)next,");
                sb.AppendLine($"                        cancellationToken)),");
                sb.AppendLine();
            }

            sb.AppendLine("                _ => throw new InvalidOperationException(");
            sb.AppendLine("                    $\"No source-generated behavior invoker for {behaviorType.Name} on {requestType.Name}.\")");
            sb.AppendLine("            };");
        }
        else
        {
            sb.AppendLine("            throw new InvalidOperationException(\"No behaviors discovered at compile time.\");");
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    /// <summary>
    /// Generates a safe method name from a fully qualified type name.
    /// </summary>
    private static string GetSafeTypeName(string fullyQualifiedType)
    {
        return fullyQualifiedType
            .Replace("global::", "")
            .Replace(".", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace(" ", "");
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
    bool HasDataAnnotations = false);

internal sealed record NotificationHandlerInterfaceInfo(
    string InterfaceType,
    string NotificationType,
    int? Order);

internal sealed record NotificationTypeInfo(
    string TypeName,
    int ExecutionStrategy,
    int ErrorStrategy);

internal sealed record BehaviorInfo(
    string ClassName,
    string Namespace,
    List<BehaviorInterfaceInfo> BehaviorInterfaces,
    bool IsOpenGeneric);

internal sealed record BehaviorInterfaceInfo(
    string InterfaceType,
    string? RequestType,
    string? ResponseType,
    bool IsOpenGeneric);

internal sealed record ExpandedBehaviorInfo(
    string BehaviorTypeName,
    string RequestType,
    string ResponseType,
    string InterfaceType);

internal sealed record ValidatorInfo(
    string ClassName,
    string Namespace,
    string InterfaceType,
    string RequestType);
