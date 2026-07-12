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

    /// <summary>
    /// Reported when FluentValidation validators are discovered for handled request types
    /// but the MediatorLite.FluentValidation package (which provides the
    /// <c>FluentValidationBehavior&lt;,&gt;</c> used by the generated pipeline) is not referenced.
    /// </summary>
    private static readonly DiagnosticDescriptor MissingFluentValidationPackage = new(
        id: "MEDL1001",
        title: "MediatorLite.FluentValidation package is not referenced",
        messageFormat: "FluentValidation validators were found but the MediatorLite.FluentValidation "
            + "package is not referenced. Add a reference to MediatorLite.FluentValidation so the "
            + "generated validation pipeline can resolve FluentValidationBehavior<,>.",
        category: "MediatorLite.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reported when an open generic pipeline behavior cannot be expanded over the discovered
    /// request types: expansion substitutes each (request, response) pair for the class's type
    /// parameters, which is only valid for the canonical
    /// <c>Behavior&lt;TRequest, TResponse&gt; : IPipelineBehavior&lt;TRequest, TResponse&gt;</c>
    /// shape with no extra constraints. Emitting anything else would produce generated code
    /// that fails to compile, so the behavior is skipped and surfaced here instead.
    /// </summary>
    private static readonly DiagnosticDescriptor UnsupportedOpenBehaviorShape = new(
        id: "MEDL1002",
        title: "Open generic pipeline behavior has an unsupported shape",
        messageFormat: "Pipeline behavior '{0}' has type parameters that cannot be bound from its "
            + "IPipelineBehavior<,> interface. Generic behavior classes are only supported in the canonical "
            + "shape Behavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> "
            + "with no constraints beyond 'where TRequest : IRequest<TResponse>'. The behavior was not "
            + "registered. Close the behavior over concrete types (as a non-generic class) or use the "
            + "canonical open shape.",
        category: "MediatorLite.Behaviors",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reported when more than one handler class implements
    /// <c>IRequestHandler&lt;TRequest, TResponse&gt;</c> for the same (request, response) pair.
    /// All of them are registered, but dispatch resolves the interface from DI, so only the
    /// last registration is ever invoked — the others are silently dead code.
    /// </summary>
    private static readonly DiagnosticDescriptor DuplicateRequestHandlers = new(
        id: "MEDL1003",
        title: "Multiple handlers registered for the same request type",
        messageFormat: "Request type '{0}' has multiple handlers ({1}); only '{2}' (the last registered) "
            + "is invoked at dispatch time. Remove the unused handler(s) or split the request types.",
        category: "MediatorLite.Handlers",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reported when a handler class is generic or nested inside a generic type. Its
    /// fully-qualified name contains unbound type parameters, so emitting it into the
    /// generated registration/dispatch would produce code that fails to compile (CS0246)
    /// inside a .g.cs file. The handler is skipped and surfaced here instead.
    /// </summary>
    private static readonly DiagnosticDescriptor UnsupportedGenericHandler = new(
        id: "MEDL1004",
        title: "Generic handler classes are not supported",
        messageFormat: "Handler '{0}' is generic or nested inside a generic type, so the source "
            + "generator cannot emit a closed registration for it; the handler was not registered. "
            + "Make the handler a non-generic class that is not nested inside a generic type.",
        category: "MediatorLite.Handlers",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

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

        // Project the only compilation-level fact Execute needs into a stable bool instead of
        // combining the Compilation itself: a raw Compilation is a new instance on every edit
        // and would force the output node to rerun on every keystroke.
        var hasFluentValidationReference = context.CompilationProvider
            .Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName("MediatorLite.FluentValidation.FluentValidationBehavior`2") is not null);

        var combinedData = handlerDeclarations.Collect()
            .Combine(notificationDeclarations.Collect())
            .Combine(behaviorDeclarations.Collect())
            .Combine(validatorDeclarations.Collect())
            .Combine(assemblyDefaults)
            .Combine(hasFluentValidationReference);

        // Generate the output
        context.RegisterSourceOutput(combinedData, static (spc, source) =>
        {
            var (((((handlers, notifications), behaviors), validators), defaults), hasFluentValidation) = source;
            Execute(spc, handlers!, notifications!, behaviors!, validators!, defaults, hasFluentValidation);
        });
    }

    /// <summary>
    /// Matches an attribute by simple name AND containing namespace. Matching on
    /// <c>AttributeClass.Name</c> alone would let a same-named attribute from any other
    /// namespace silently change generator behavior (e.g. a foreign
    /// <c>DisableMediatorLoggingAttribute</c> turning off all generated logging).
    /// </summary>
    private static bool IsMediatorLiteAttribute(AttributeData attr, string simpleName)
        => attr.AttributeClass is { } attrClass
           && attrClass.Name == simpleName
           && attrClass.ContainingNamespace?.ToDisplayString() == "MediatorLite";

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
            if (IsMediatorLiteAttribute(attr, "DefaultNotificationExecutionAttribute")
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is int es)
            {
                execution = es;
            }
            else if (IsMediatorLiteAttribute(attr, "DefaultNotificationErrorAttribute")
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is int ers)
            {
                error = ers;
            }
            else if (IsMediatorLiteAttribute(attr, "DisableMediatorLoggingAttribute"))
            {
                loggingDisabled = true;
            }
            else if (IsMediatorLiteAttribute(attr, "DisableMediatorTracingAttribute"))
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
        // Classes and (reference-type) records alike can implement handler interfaces;
        // record structs are excluded because DI implementation types must be classes.
        return node is (ClassDeclarationSyntax or RecordDeclarationSyntax) and TypeDeclarationSyntax typeDecl
               && !node.IsKind(SyntaxKind.RecordStructDeclaration)
               && typeDecl.BaseList is not null
               && !typeDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);
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

        var (executionStrategy, errorStrategy) = ReadNotificationStrategyAttributes(typeSymbol);

        if (executionStrategy is null && errorStrategy is null)
            return null;

        return new NotificationTypeInfo(
            TypeName: typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ExecutionStrategy: executionStrategy,
            ErrorStrategy: errorStrategy);
    }

    /// <summary>
    /// Reads <c>[NotificationExecution]</c> / <c>[NotificationError]</c> off a notification
    /// type symbol. Symbol-based (not syntax-based) so it works for notification types declared
    /// in referenced assemblies, whose declaration syntax is not part of this compilation.
    /// </summary>
    private static (int? ExecutionStrategy, int? ErrorStrategy) ReadNotificationStrategyAttributes(ITypeSymbol typeSymbol)
    {
        int? executionStrategy = null;
        int? errorStrategy = null;

        foreach (var attr in typeSymbol.GetAttributes())
        {
            if (IsMediatorLiteAttribute(attr, "NotificationExecutionAttribute")
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is int es)
            {
                executionStrategy = es;
            }
            else if (IsMediatorLiteAttribute(attr, "NotificationErrorAttribute")
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is int ers)
            {
                errorStrategy = ers;
            }
        }

        return (executionStrategy, errorStrategy);
    }

    private static bool IsBehaviorCandidate(SyntaxNode node) => IsHandlerCandidate(node);

    private static BehaviorInfo? GetBehaviorInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;

        if (classSymbol is null || classSymbol.IsAbstract || !classSymbol.IsReferenceType)
            return null;

        var hasSkipAttribute = classSymbol.GetAttributes()
            .Any(a => IsMediatorLiteAttribute(a, "MediatorGenerationAttribute")
                      && a.NamedArguments.Any(arg => arg.Key == "Skip" && arg.Value.Value is true));

        if (hasSkipAttribute)
            return null;

        var behaviorInterfaces = new List<BehaviorInterfaceInfo>();
        bool isOpenGeneric = classSymbol.IsGenericType && classSymbol.IsUnboundGenericType == false
                             && classSymbol.TypeParameters.Length > 0;
        bool hasUnsupportedOpenShape = false;

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

                    // Open interfaces are expanded by substituting each discovered
                    // (request, response) pair for the class's type parameters. That is only
                    // valid for the canonical shape Behavior<TRequest, TResponse> with no
                    // constraints beyond `where TRequest : IRequest<TResponse>` — anything
                    // else would emit closed types that do not compile (wrong arity, swapped
                    // parameters, or unsatisfied constraints). Such behaviors are skipped and
                    // surfaced via MEDL1002 instead.
                    if (isInterfaceOpen && !IsSupportedOpenShape(classSymbol, iface))
                    {
                        hasUnsupportedOpenShape = true;
                        continue;
                    }

                    // Non-canonical shapes where a type parameter is not a *top-level*
                    // interface argument also cannot be emitted:
                    //  - a parameter nested inside an argument (IPipelineBehavior<Wrap<T>, R>)
                    //    would emit `Wrap<T>` with T unbound;
                    //  - a generic class whose interface is fully closed
                    //    (Behavior<TUnused> : IPipelineBehavior<Req, Res>) would emit the
                    //    class display name `Behavior<TUnused>` with TUnused unbound —
                    //    the same applies when the behavior is nested inside a generic
                    //    type (Outer<T>.Inner), hence HasTypeParametersInScope.
                    // All produce CS0246 in the generated files, so they get MEDL1002 too.
                    if (!isInterfaceOpen
                        && (typeArgs.Any(ContainsTypeParameter) || HasTypeParametersInScope(classSymbol)))
                    {
                        hasUnsupportedOpenShape = true;
                        continue;
                    }

                    behaviorInterfaces.Add(new BehaviorInterfaceInfo(
                        InterfaceType: iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        RequestType: isInterfaceOpen ? null : typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ResponseType: isInterfaceOpen ? null : typeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        IsOpenGeneric: isInterfaceOpen));
                }
            }
        }

        if (behaviorInterfaces.Count == 0 && !hasUnsupportedOpenShape)
            return null;

        // Extract BehaviorOrderAttribute if present
        int behaviorOrder = 0;
        var orderAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => IsMediatorLiteAttribute(a, "BehaviorOrderAttribute"));
        if (orderAttr != null && orderAttr.ConstructorArguments.Length > 0)
        {
            behaviorOrder = (int)(orderAttr.ConstructorArguments[0].Value ?? 0);
        }

        return new BehaviorInfo(
            ClassName: classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace: classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
            BehaviorInterfaces: new EquatableArray<BehaviorInterfaceInfo>(behaviorInterfaces.ToArray()),
            IsOpenGeneric: isOpenGeneric,
            Order: behaviorOrder,
            HasUnsupportedOpenShape: hasUnsupportedOpenShape);
    }

    /// <summary>
    /// A behavior's open <c>IPipelineBehavior&lt;,&gt;</c> interface can be expanded over all
    /// discovered request/response pairs only when the class has exactly two type parameters
    /// used directly, in order, as the interface's type arguments, and the parameters carry no
    /// constraints beyond the interface-mandated <c>where TRequest : IRequest&lt;TResponse&gt;</c>.
    /// </summary>
    private static bool IsSupportedOpenShape(INamedTypeSymbol classSymbol, INamedTypeSymbol iface)
    {
        if (classSymbol.TypeParameters.Length != 2)
            return false;

        // A behavior nested inside a generic type cannot be expanded: its display name is
        // truncated at the *first* '<' (the outer type's), so the emitted closed type would
        // name the outer type instead of the behavior.
        if (classSymbol.ContainingType is { } containing && HasTypeParametersInScope(containing))
            return false;

        var requestParam = classSymbol.TypeParameters[0];
        var responseParam = classSymbol.TypeParameters[1];

        if (!SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], requestParam)
            || !SymbolEqualityComparer.Default.Equals(iface.TypeArguments[1], responseParam))
        {
            return false;
        }

        if (HasSpecialConstraints(requestParam) || HasSpecialConstraints(responseParam)
            || responseParam.ConstraintTypes.Length > 0)
        {
            return false;
        }

        foreach (var constraint in requestParam.ConstraintTypes)
        {
            var isRequestConstraint = constraint is INamedTypeSymbol { TypeArguments.Length: 1 } named
                && named.OriginalDefinition.ToDisplayString() == "MediatorLite.IRequest<TResponse>"
                && SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], responseParam);

            if (!isRequestConstraint)
                return false;
        }

        return true;

        static bool HasSpecialConstraints(ITypeParameterSymbol parameter)
            => parameter.HasReferenceTypeConstraint
               || parameter.HasValueTypeConstraint
               || parameter.HasNotNullConstraint
               || parameter.HasUnmanagedTypeConstraint
               || parameter.HasConstructorConstraint;
    }

    /// <summary>
    /// Whether the type — or any type it is nested inside — declares type parameters. The
    /// fully-qualified display string of such a type contains those parameters (e.g.
    /// <c>Handler&lt;TUnused&gt;</c> or <c>Outer&lt;T&gt;.Inner</c>), so emitting it into the
    /// generated registration or dispatch produces unbound identifiers (CS0246). Discovery
    /// must skip these types (surfacing a diagnostic) instead of emitting broken code.
    /// Checked explicitly across the containing-type chain rather than relying on
    /// <c>INamedTypeSymbol.IsGenericType</c> semantics.
    /// </summary>
    private static bool HasTypeParametersInScope(INamedTypeSymbol symbol)
    {
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.TypeParameters.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a type parameter occurs anywhere inside <paramref name="type"/> — not just at
    /// the top level. Emitting any type that still contains a type parameter into the generated
    /// registration or pipeline produces unbound identifiers (CS0246), so such shapes must be
    /// rejected rather than emitted.
    /// </summary>
    private static bool ContainsTypeParameter(ITypeSymbol type)
        => type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter),
            _ => false,
        };

    private static bool IsValidatorCandidate(SyntaxNode node) => IsHandlerCandidate(node);

    private static ValidatorInfo? GetValidatorInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;

        if (classSymbol is null || classSymbol.IsAbstract || !classSymbol.IsReferenceType)
            return null;

        // Skip generic validators (e.g., a user's own generic AbstractValidator<T>) and
        // validators nested inside generic types — their display names carry unbound type
        // parameters and cannot be emitted.
        if (HasTypeParametersInScope(classSymbol))
            return null;

        var hasSkipAttribute = classSymbol.GetAttributes()
            .Any(a => IsMediatorLiteAttribute(a, "MediatorGenerationAttribute")
                      && a.NamedArguments.Any(arg => arg.Key == "Skip" && arg.Value.Value is true));

        if (hasSkipAttribute)
            return null;

        // A validator class may implement IValidator<T> for several request types; collect
        // every match — stopping at the first would silently leave the remaining request
        // types unvalidated.
        List<ValidatorTargetInfo>? targets = null;

        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (!iface.IsGenericType)
                continue;

            // Match FluentValidation.IValidator<T> by namespace + name so discovery does
            // not depend on the interface's type-parameter name. Concrete validators
            // surface this interface via AbstractValidator<T>. The non-generic
            // FluentValidation.IValidator is skipped by the IsGenericType guard above.
            if (iface.Name != "IValidator"
                || iface.TypeArguments.Length != 1
                || iface.ContainingNamespace?.ToDisplayString() != "FluentValidation")
            {
                continue;
            }

            var typeArgs = iface.TypeArguments;

            // Only discover validators for concrete (non-generic) request types.
            if (typeArgs[0].TypeKind == TypeKind.TypeParameter)
                continue;

            (targets ??= []).Add(new ValidatorTargetInfo(
                InterfaceType: iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                RequestType: typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        if (targets is null)
            return null;

        return new ValidatorInfo(
            ClassName: classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace: classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
            Targets: new EquatableArray<ValidatorTargetInfo>(targets.ToArray()));
    }

    /// <summary>
    /// Collects the fully-qualified names of all base classes (excluding object) and all
    /// implemented interfaces of a type. Used to order switch arms most-derived-first so
    /// type-pattern dispatch preserves runtime-type specificity when message types inherit
    /// from each other. Interfaces are included because message types can themselves be
    /// interfaces (e.g. <c>IRequestHandler&lt;IFoo, T&gt;</c>): without them, a concrete
    /// implementor's arm could be emitted after the interface's arm and be subsumed (CS8120).
    /// </summary>
    private static EquatableArray<string> GetBaseTypeNames(ITypeSymbol type)
    {
        var result = new List<string>();
        var current = type.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            result.Add(current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            current = current.BaseType;
        }
        foreach (var iface in type.AllInterfaces)
        {
            result.Add(iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }
        return new EquatableArray<string>(result.ToArray());
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
        Func<T, EquatableArray<string>> baseTypes)
    {
        var dispatchedTypes = new HashSet<string>(items.Select(typeName));
        return items
            .Select((item, index) => (Item: item, Index: index,
                Depth: baseTypes(item).Count(dispatchedTypes.Contains)))
            .OrderByDescending(x => x.Depth)
            .ThenBy(x => x.Index)
            .Select(x => x.Item)
            .ToList();
    }

    private static HandlerInfo? GetHandlerInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;

        if (classSymbol is null || classSymbol.IsAbstract || !classSymbol.IsReferenceType)
            return null;

        var hasSkipAttribute = classSymbol.GetAttributes()
            .Any(a => IsMediatorLiteAttribute(a, "MediatorGenerationAttribute")
                      && a.NamedArguments.Any(arg => arg.Key == "Skip" && arg.Value.Value is true));

        if (hasSkipAttribute)
            return null;

        int? handlerOrder = null;
        var orderAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => IsMediatorLiteAttribute(a, "NotificationHandlerOrderAttribute"));
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
                    requestHandlerInterfaces.Add(new HandlerInterfaceInfo(
                        InterfaceType: iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        RequestType: typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ResponseType: typeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BaseTypes: GetBaseTypeNames(typeArgs[0])));
                }
            }
            else if (originalDef == "MediatorLite.INotificationHandler<TNotification>")
            {
                var typeArgs = iface.TypeArguments;
                if (typeArgs.Length == 1)
                {
                    // Strategy attributes are read from the notification type symbol here (not
                    // only from the notification-declaration syntax pipeline) so they are
                    // honored when the notification type lives in a referenced assembly.
                    var (notifExecution, notifError) = ReadNotificationStrategyAttributes(typeArgs[0]);

                    notificationHandlerInterfaces.Add(new NotificationHandlerInterfaceInfo(
                        InterfaceType: iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        NotificationType: typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Order: handlerOrder,
                        BaseTypes: GetBaseTypeNames(typeArgs[0]),
                        ExecutionStrategy: notifExecution,
                        ErrorStrategy: notifError));
                }
            }
        }

        if (requestHandlerInterfaces.Count == 0 && notificationHandlerInterfaces.Count == 0)
            return null;

        // A generic handler (or one nested inside a generic type) cannot be emitted: its
        // display name carries unbound type parameters, producing CS0246 in the generated
        // files. Keep the class name for the MEDL1004 report but carry no interfaces so
        // nothing downstream registers or dispatches it.
        if (HasTypeParametersInScope(classSymbol))
        {
            return new HandlerInfo(
                ClassName: classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Namespace: classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                RequestHandlers: new EquatableArray<HandlerInterfaceInfo>([]),
                NotificationHandlers: new EquatableArray<NotificationHandlerInterfaceInfo>([]),
                HasUnsupportedGenericShape: true);
        }

        return new HandlerInfo(
            ClassName: classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace: classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
            RequestHandlers: new EquatableArray<HandlerInterfaceInfo>(requestHandlerInterfaces.ToArray()),
            NotificationHandlers: new EquatableArray<NotificationHandlerInterfaceInfo>(notificationHandlerInterfaces.ToArray()));
    }

    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<HandlerInfo?> handlers,
        ImmutableArray<NotificationTypeInfo?> notifications,
        ImmutableArray<BehaviorInfo?> behaviors,
        ImmutableArray<ValidatorInfo?> validators,
        AssemblyDefaults assemblyDefaults,
        bool hasFluentValidationReference)
    {
        // Dedupe by fully-qualified name: the syntax providers run the transform once per
        // declaration node, so a partial type whose declarations each carry a base list is
        // discovered once per declaration. Without this, a partial notification handler would
        // be registered — and invoked — once per part.
        var validHandlers = DistinctByName(handlers, h => h.ClassName);
        var validNotifications = DistinctByName(notifications, n => n.TypeName);
        var validBehaviors = DistinctByName(behaviors, b => b.ClassName);
        var validValidators = DistinctByName(validators, v => v.ClassName);

        foreach (var behavior in validBehaviors.Where(b => b.HasUnsupportedOpenShape))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedOpenBehaviorShape, Location.None, StripGlobalPrefix(behavior.ClassName)));
        }

        foreach (var handler in validHandlers.Where(h => h.HasUnsupportedGenericShape))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedGenericHandler, Location.None, StripGlobalPrefix(handler.ClassName)));
        }

        validHandlers = validHandlers.Where(h => !h.HasUnsupportedGenericShape).ToList();

        if (validHandlers.Count == 0)
        {
            GenerateEmptyRegistration(context);
            return;
        }

        // Silent last-wins is easy to hit by accident (a copied handler class left behind):
        // surface every duplicated (request, response) pair so the dead handlers are visible.
        var duplicateGroups = validHandlers
            .SelectMany(h => h.RequestHandlers.Select(r => (Handler: h, Interface: r)))
            .GroupBy(x => (x.Interface.RequestType, x.Interface.ResponseType))
            .Where(g => g.Count() > 1);
        foreach (var group in duplicateGroups)
        {
            var handlerNames = group.Select(x => StripGlobalPrefix(x.Handler.ClassName)).ToList();
            context.ReportDiagnostic(Diagnostic.Create(
                DuplicateRequestHandlers,
                Location.None,
                StripGlobalPrefix(group.Key.RequestType),
                string.Join(", ", handlerNames),
                handlerNames[handlerNames.Count - 1]));
        }

        var expandedBehaviors = ExpandBehaviors(validBehaviors, validHandlers);

        // Determine which request types need validation
        var requestTypesWithValidation = DetermineValidationTargets(validHandlers, validValidators);

        // FluentValidation validators were discovered but the MediatorLite.FluentValidation
        // package (which carries FluentValidationBehavior<,>) is not referenced. Emitting the
        // behavior would fail to compile with a cryptic error, and silently skipping it would
        // let validation stop running unnoticed. Report a clear build error (MEDL1001) and skip
        // only the validation wiring — the rest of the dispatch still generates and compiles, so
        // the developer sees exactly one actionable error rather than a cascade of missing-type
        // errors. MEDL1001 is error-severity, so validation can never silently fail to run.
        if (requestTypesWithValidation.Count > 0
            && !hasFluentValidationReference)
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingFluentValidationPackage, Location.None));
            requestTypesWithValidation = new List<(string RequestType, string ResponseType)>();
        }

        // Add FluentValidationBehavior entries as the outermost pipeline arm.
        foreach (var (requestType, responseType) in requestTypesWithValidation)
        {
            expandedBehaviors.Add(new ExpandedBehaviorInfo(
                BehaviorTypeName: $"global::MediatorLite.FluentValidation.FluentValidationBehavior<{requestType}, {responseType}>",
                RequestType: requestType,
                ResponseType: responseType,
                InterfaceType: $"global::MediatorLite.IPipelineBehavior<{requestType}, {responseType}>"));
        }

        GenerateRegistrationCode(context, validHandlers, expandedBehaviors, validValidators, requestTypesWithValidation);
        GenerateSourceGeneratedMediator(context, validHandlers, validNotifications, validBehaviors, expandedBehaviors, assemblyDefaults);
    }

    /// <summary>
    /// Filters out nulls and keeps the first occurrence per fully-qualified name.
    /// See the call site in <see cref="Execute"/> for why duplicates can occur (partial types).
    /// </summary>
    private static List<T> DistinctByName<T>(ImmutableArray<T?> items, Func<T, string> name)
        where T : class
    {
        var seen = new HashSet<string>();
        var result = new List<T>();
        foreach (var item in items)
        {
            if (item is not null && seen.Add(name(item)))
            {
                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>
    /// Determines which request types need validation based on the discovered
    /// FluentValidation validators.
    /// </summary>
    private static List<(string RequestType, string ResponseType)> DetermineValidationTargets(
        List<HandlerInfo> handlers,
        List<ValidatorInfo> validators)
    {
        var result = new List<(string RequestType, string ResponseType)>();
        var requestTypesWithValidators = new HashSet<string>(
            validators.SelectMany(v => v.Targets).Select(t => t.RequestType));

        var requestResponsePairs = handlers
            .SelectMany(h => h.RequestHandlers.Select(r => (r.RequestType, ResponseType: r.ResponseType!)))
            .Distinct()
            .ToList();

        foreach (var (requestType, responseType) in requestResponsePairs)
        {
            if (requestTypesWithValidators.Contains(requestType))
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
        sb.AppendLine("        /// Adds only source-generated FluentValidation validators to the service collection.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine(
            "        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedValidators(");
        sb.AppendLine("            this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("        {");

        // Register discovered FluentValidation validators against their FluentValidation.IValidator<T>
        // service type so FluentValidationBehavior<T,_> can resolve them. No assembly scanning.
        // A class registers once per IValidator<T> it implements. Only registrations whose
        // request type has a generated pipeline (i.e. a handler in this compilation) are
        // emitted: a validator for an unhandled request type is never resolved, so
        // registering it would be dead wiring (and could reference inaccessible types).
        var validatedRequestTypes = new HashSet<string>(requestTypesWithValidation.Select(t => t.RequestType));
        var validatorRegistrations = validators
            .SelectMany(v => v.Targets.Select(t => (v.ClassName, t.InterfaceType, t.RequestType)))
            .Where(r => validatedRequestTypes.Contains(r.RequestType))
            .ToList();

        foreach (var (className, interfaceType, _) in validatorRegistrations)
        {
            sb.AppendLine($"            services.AddTransient<{interfaceType}, {className}>();");
        }

        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // --- Behavior registration ---
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Adds only source-generated pipeline behaviors to the service collection.");
        sb.AppendLine("        /// FluentValidationBehavior is registered first to ensure validation runs before other behaviors.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine(
            "        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedBehaviors(");
        sb.AppendLine("            this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("        {");

        // Register FluentValidationBehavior FIRST for request types with validators.
        // Register by concrete type so the unrolled pipeline can resolve each behavior individually.
        if (requestTypesWithValidation.Count > 0)
        {
            sb.AppendLine("            // Validation behaviors (registered first to ensure validation runs before other behaviors)");
            foreach (var (requestType, responseType) in requestTypesWithValidation)
            {
                sb.AppendLine($"            services.AddTransient<global::MediatorLite.FluentValidation.FluentValidationBehavior<{requestType}, {responseType}>>();");
            }
            sb.AppendLine();
        }

        // Then register other (non-validation) behaviors by concrete type
        if (expandedBehaviors.Count > 0)
        {
            var nonValidationBehaviors = expandedBehaviors
                .Where(b => !b.BehaviorTypeName.StartsWith("global::MediatorLite.FluentValidation.FluentValidationBehavior<"))
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
        var totalValidatorCount = validatorRegistrations.Count;
        var nonValidationBehaviorCount = expandedBehaviors
            .Count(b => !b.BehaviorTypeName.StartsWith("global::MediatorLite.FluentValidation.FluentValidationBehavior<"));

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

        // Group behaviors by request type for unrolled pipeline generation.
        // Validation behaviors are forced to the front so they wrap (run before) every other
        // behavior — this is the documented invariant that invalid requests short-circuit before
        // any other behavior runs. [BehaviorOrder] only orders the non-validation behaviors.
        var behaviorsByRequest = expandedBehaviors
            .GroupBy(b => (b.RequestType, b.ResponseType))
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(b => b.BehaviorTypeName.StartsWith("global::MediatorLite.FluentValidation.FluentValidationBehavior<") ? 0 : 1)
                    .ThenBy(b => b.Order)
                    .ToList());

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

        // One switch arm per request type. Duplicate handlers for the same (request, response)
        // pair collapse to a single entry — resolution via IRequestHandler<,> picks the last DI
        // registration, matching previous behavior. A request type may implement IRequest<T>
        // for several T (each with its own handler); every distinct response type keeps its own
        // fully-typed Send_* pipeline inside the shared arm, selected by the TResponse guard.
        var dispatchGroups = requestHandlers
            .GroupBy(rh => rh.Interface.RequestType)
            .Select(g => (
                RequestType: g.Key,
                BaseTypes: g.First().Interface.BaseTypes,
                Entries: g.GroupBy(x => x.Interface.ResponseType).Select(rg => rg.First()).ToList()))
            .ToList();
        dispatchGroups = SortMostDerivedFirst(
            dispatchGroups,
            g => g.RequestType,
            g => g.BaseTypes);

        // Method-name suffixes are sanitized type names, and sanitization can collide
        // (App.Get_User vs App.Get.User both become App_Get_User). Distinct request types
        // merely overload Send_*, but a multi-response request whose response names collide
        // would emit two methods differing only by return type (CS0111). Uniquify every
        // suffix deterministically up front; both the switch arms and the pipeline methods
        // read from these maps so the names always stay in sync.
        var sendMethodSuffixes = new Dictionary<(string RequestType, string ResponseType), string>();
        var usedSendSuffixes = new HashSet<string>();
        foreach (var group in dispatchGroups)
        {
            var multipleResponses = group.Entries.Count > 1;
            foreach (var (_, iface) in group.Entries)
            {
                var baseSuffix = multipleResponses
                    ? $"{GetSafeTypeName(group.RequestType)}_{GetSafeTypeName(iface.ResponseType!)}"
                    : GetSafeTypeName(group.RequestType);
                var suffix = baseSuffix;
                var counter = 2;
                while (!usedSendSuffixes.Add(suffix))
                {
                    suffix = $"{baseSuffix}_{counter++}";
                }

                sendMethodSuffixes[(group.RequestType, iface.ResponseType!)] = suffix;
            }
        }

        var publishMethodSuffixes = new Dictionary<string, string>();
        var usedPublishSuffixes = new HashSet<string>();
        foreach (var group in notificationGroups)
        {
            var baseSuffix = GetSafeTypeName(group.NotificationType);
            var suffix = baseSuffix;
            var counter = 2;
            while (!usedPublishSuffixes.Add(suffix))
            {
                suffix = $"{baseSuffix}_{counter++}";
            }

            publishMethodSuffixes[group.NotificationType] = suffix;
        }

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
        foreach (var group in dispatchGroups)
        {
            var safeName = GetSafeTypeName(group.RequestType);
            sb.AppendLine($"                case {group.RequestType} r_{safeName}:");
            sb.AppendLine("                {");

            if (group.Entries.Count == 1)
            {
                var iface = group.Entries[0].Interface;
                var sendSuffix = sendMethodSuffixes[(group.RequestType, iface.ResponseType!)];
                sb.AppendLine($"                    var vt = Send_{sendSuffix}(r_{safeName}, cancellationToken);");
                sb.AppendLine($"                    if (typeof(TResponse) == typeof({iface.ResponseType}))");
                sb.AppendLine("                    {");
                sb.AppendLine("                        // SAFETY: guarded by the exact type-equality check above, this is an");
                sb.AppendLine("                        // identity reinterpret (ValueTask<T> as ValueTask<T>). The guard is");
                sb.AppendLine("                        // JIT-folded to a constant, so the cast itself is free.");
                sb.AppendLine($"                        return Unsafe.As<ValueTask<{iface.ResponseType}>, ValueTask<TResponse>>(ref vt);");
                sb.AppendLine("                    }");
                sb.AppendLine($"                    return SlowCast<{iface.ResponseType}, TResponse>(vt);");
            }
            else
            {
                foreach (var (_, iface) in group.Entries)
                {
                    var sendName = $"Send_{sendMethodSuffixes[(group.RequestType, iface.ResponseType!)]}";
                    sb.AppendLine($"                    if (typeof(TResponse) == typeof({iface.ResponseType}))");
                    sb.AppendLine("                    {");
                    sb.AppendLine("                        // SAFETY: identity reinterpret guarded by the exact type-equality check.");
                    sb.AppendLine($"                        var vt = {sendName}(r_{safeName}, cancellationToken);");
                    sb.AppendLine($"                        return Unsafe.As<ValueTask<{iface.ResponseType}>, ValueTask<TResponse>>(ref vt);");
                    sb.AppendLine("                    }");
                }

                sb.AppendLine("                    // Covariant dispatch (IRequest<out T>): pick the pipeline whose concrete");
                sb.AppendLine("                    // response type is assignable to the requested TResponse. Value-type");
                sb.AppendLine("                    // responses are excluded: variance only exists for reference conversions,");
                sb.AppendLine("                    // so an IRequest<TResponse> reference can never originate from a");
                sb.AppendLine("                    // value-type response interface — but IsAssignableTo alone would match");
                sb.AppendLine("                    // one via boxing and silently run the wrong pipeline.");
                foreach (var (_, iface) in group.Entries)
                {
                    var sendName = $"Send_{sendMethodSuffixes[(group.RequestType, iface.ResponseType!)]}";
                    sb.AppendLine($"                    if (!typeof({iface.ResponseType}).IsValueType && typeof({iface.ResponseType}).IsAssignableTo(typeof(TResponse)))");
                    sb.AppendLine("                    {");
                    sb.AppendLine($"                        return SlowCast<{iface.ResponseType}, TResponse>({sendName}(r_{safeName}, cancellationToken));");
                    sb.AppendLine("                    }");
                }

                sb.AppendLine("                    throw new InvalidOperationException(");
                sb.AppendLine($"                        $\"Request type {StripGlobalPrefix(group.RequestType)} has no handler producing response type {{typeof(TResponse).FullName}}.\");");
            }

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
            var publishSuffix = publishMethodSuffixes[group.NotificationType];
            sb.AppendLine($"                case {group.NotificationType} n_{safeName}:");
            sb.AppendLine($"                    return Publish_{publishSuffix}(n_{safeName}, cancellationToken);");
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

        foreach (var group in dispatchGroups)
        {
            foreach (var (_, iface) in group.Entries)
            {
                var requestType = iface.RequestType;
                var responseType = iface.ResponseType!;

                // The suffix map is shared with the switch-arm emission above, so names stay
                // in sync (including any collision-disambiguation suffix).
                var safeName = sendMethodSuffixes[(requestType, responseType)];

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
            // Shared with the switch-arm emission above, so names stay in sync (including any
            // collision-disambiguation suffix) — do not re-derive with GetSafeTypeName here.
            var safeName = publishMethodSuffixes[notificationType];
            var handlersForNotification = group.Handlers;

            notificationsByType.TryGetValue(notificationType, out var perTypeOptions);
            if (perTypeOptions is null)
            {
                // The notification type is declared in a referenced assembly, so the
                // syntax-based pipeline never saw it. Fall back to the strategies the handler
                // discovery read off the type symbol (identical for in-compilation types).
                var carried = handlersForNotification[0].Interface;
                if (carried.ExecutionStrategy is not null || carried.ErrorStrategy is not null)
                {
                    perTypeOptions = new NotificationTypeInfo(
                        notificationType, carried.ExecutionStrategy, carried.ErrorStrategy);
                }
            }

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
        var simpleRequest = GetSimpleDisplayName(requestType);

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
        var simpleNotification = GetSimpleDisplayName(notificationType);

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
                // StopOnFirst returns from inside the strategy body on the first successful
                // handler, bypassing the wrapper's post-body success log — so the success log
                // statement is passed in and emitted before each early return instead.
                var successLogStatement = loggingEnabled
                    ? $"__logger.LogDebug(\"Notification {{NotificationType}} published successfully\", \"{simpleNotification}\");"
                    : null;
                GenerateStopOnFirstNotificationExecution(sb, notificationType, handlers, errorStrategy, successLogStatement);
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
                sb.AppendLine("            // Only genuine cancellation of the publish token stops the loop; a handler's");
                sb.AppendLine("            // own OperationCanceledException is an ordinary fault to aggregate.");
                sb.AppendLine("            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }");
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
        // An already-cancelled publish token must be rejected before any handler starts,
        // matching the entry check every other execution strategy performs. In-flight
        // cancellation stays cooperative (handlers observe the token themselves).
        sb.AppendLine("            ct.ThrowIfCancellationRequested();");

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
            sb.AppendLine("                // Genuine cancellation of the publish token dominates and surfaces unwrapped.");
            sb.AppendLine("                ct.ThrowIfCancellationRequested();");
            sb.AppendLine("                // Otherwise every fault is aggregated — including a handler's own");
            sb.AppendLine("                // OperationCanceledException. Rethrowing an OCE unwrapped here would");
            sb.AppendLine("                // silently drop its siblings' faults.");
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
        int errorStrategy,
        string? successLogStatement = null)
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
                if (successLogStatement is not null)
                {
                    sb.AppendLine($"                {successLogStatement}");
                }
                sb.AppendLine("                return; // Success — stop here");
                sb.AppendLine("            }");
                sb.AppendLine("            // Only genuine cancellation of the publish token stops the fallback chain; a");
                sb.AppendLine("            // handler's own OperationCanceledException is an ordinary fault to aggregate.");
                sb.AppendLine("            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }");
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
    /// Converts a fully qualified type name to a safe C# identifier fragment.
    /// Every character that is not a letter, digit, or underscore is mapped to '_' so the
    /// result is always a valid identifier fragment — a whitelist of a few punctuation
    /// characters is not enough once response type names reach the method name (e.g. an
    /// array response <c>int[]</c> or a tuple <c>(int, string)</c> carries '[', ']', '(', ')').
    /// Callers always prefix the result (Send_, Publish_, r_, n_), so a leading digit is fine.
    /// </summary>
    private static string GetSafeTypeName(string typeName)
    {
        var cleaned = typeName.Replace("global::", "");
        var sb = new StringBuilder(cleaned.Length);
        foreach (var c in cleaned)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Strips every "global::" prefix for use in emitted string literals (tag values, log
    /// message arguments, diagnostic messages). Stripping only a leading prefix would leave
    /// inner prefixes on generic type arguments (<c>Ns.Query&lt;global::System.String&gt;</c>)
    /// and on tuple types (which start with '('). Display-only — never used for emitted code,
    /// where the prefix must stay. The returned value is suitable to embed inside a C# string
    /// literal (type display strings never contain '"' or '\').
    /// </summary>
    private static string StripGlobalPrefix(string typeName)
        => typeName.Replace("global::", "");

    /// <summary>
    /// Computes the simple display name of a fully-qualified type for log messages and
    /// activity names. The namespace is stripped from the type itself, not from inside its
    /// type arguments: a plain <c>LastIndexOf('.')</c> on a generic like
    /// <c>Ns.Query&lt;System.String&gt;</c> would land inside the argument and yield "String&gt;".
    /// </summary>
    private static string GetSimpleDisplayName(string typeName)
    {
        var name = typeName.Replace("global::", "");
        var genericStart = name.IndexOf('<');
        var searchEnd = (genericStart >= 0 ? genericStart : name.Length) - 1;
        var lastDot = searchEnd >= 0 ? name.LastIndexOf('.', searchEnd) : -1;
        return lastDot >= 0 ? name.Substring(lastDot + 1) : name;
    }
}

// Pipeline model records. All collection members use EquatableArray<T> (value equality):
// List<T> members would compare by reference and permanently defeat incremental caching.

internal sealed record HandlerInfo(
    string ClassName,
    string Namespace,
    EquatableArray<HandlerInterfaceInfo> RequestHandlers,
    EquatableArray<NotificationHandlerInterfaceInfo> NotificationHandlers,
    bool HasUnsupportedGenericShape = false);

internal sealed record HandlerInterfaceInfo(
    string InterfaceType,
    string RequestType,
    string? ResponseType,
    EquatableArray<string> BaseTypes = default);

internal sealed record NotificationHandlerInterfaceInfo(
    string InterfaceType,
    string NotificationType,
    int? Order,
    EquatableArray<string> BaseTypes = default,
    int? ExecutionStrategy = null,
    int? ErrorStrategy = null);

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
    EquatableArray<BehaviorInterfaceInfo> BehaviorInterfaces,
    bool IsOpenGeneric,
    int Order = 0,
    bool HasUnsupportedOpenShape = false);

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
    EquatableArray<ValidatorTargetInfo> Targets);

internal sealed record ValidatorTargetInfo(
    string InterfaceType,
    string RequestType);
