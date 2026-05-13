---
name: mediatorlite-source-generation
description: Reference for the MediatorLite.SourceGeneration project -- HandlerDiscoveryGenerator (IIncrementalGenerator) with four discovery pipelines, emission of MediatorLite.Generated.MediatorLiteRegistration + SourceGeneratedMediator, compile-time resolution of notification strategies, unrolled pipelines, and inline logging/tracing with [assembly: DisableMediatorLogging] / [assembly: DisableMediatorTracing] opt-out. Use when modifying code generation, tuning emitted dispatch, adding new generator pipelines, or debugging generated output.
triggers: source generator, HandlerDiscoveryGenerator, IIncrementalGenerator, MediatorLiteRegistration, AddGeneratedHandlers, generated dispatch, notification strategy resolution, inline logging emission, SourceGeneratedMediator, Pipeline_, Publish_, DisableMediatorLogging, DisableMediatorTracing, unrolled pipeline, AssemblyDefaults, ResolveStrategies
---

# MediatorLite.SourceGeneration

## Purpose

`MediatorLite.SourceGeneration` is a Roslyn incremental source generator that transforms every `IRequestHandler<,>`, `INotificationHandler<>`, `IPipelineBehavior<,>`, and `IValidator<>` in the compilation into a fully unrolled, reflection-free dispatch implementation. It emits two generated files per compilation unit — `MediatorLiteRegistration.g.cs` (DI wiring + diagnostic counts) and `SourceGeneratedMediator.g.cs` (O(1) dispatch tables + per-request/per-notification methods) — and resolves every notification execution/error strategy and observability decision at compile time so the runtime `Mediator.cs` has zero branches.

## When to use

- Adding a new attribute, interface, or discovery rule (e.g. a new handler kind).
- Changing the emitted shape of `MediatorLiteRegistration` or `SourceGeneratedMediator`.
- Modifying how behaviors compose (e.g. changing ValidationBehavior's ordering, or expanding open generics differently).
- Adjusting compile-time resolution of `NotificationExecutionStrategy` / `NotificationErrorStrategy`.
- Adding new observability surfaces (e.g. metrics), or adjusting `[assembly: DisableMediatorLogging]` / `[assembly: DisableMediatorTracing]` handling.
- Debugging why a handler / validator / behavior does not appear in generated output.

## Project location & entry points

- [MediatorLite.SourceGeneration.csproj](src/MediatorLite.SourceGeneration/MediatorLite.SourceGeneration.csproj) — targets `netstandard2.0` (required for source generators), `IsRoslynComponent=true`, packages with the `analyzers/dotnet/cs` pack path.
- [HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs) — the **only** generator class; everything lives here.
- [README.md](src/MediatorLite.SourceGeneration/README.md) — public package docs.
- Consumer projects reference it as an analyzer:
  ```
  <ProjectReference Include="..\..\src\MediatorLite.SourceGeneration\MediatorLite.SourceGeneration.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  ```
- Generated output namespace: `MediatorLite.Generated`. Class names: `MediatorLiteRegistration`, `SourceGeneratedMediator`.

## Core types / API surface

### Generator entry point

```18:20:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
[Generator(LanguageNames.CSharp)]
public sealed class HandlerDiscoveryGenerator : IIncrementalGenerator
{
```

### The four discovery pipelines

`Initialize` builds four `IncrementalValuesProvider<...>` pipelines (one per artifact type) plus a fifth provider for assembly-level defaults, then combines them into a single source-output callback.

```28:76:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

All four `Is*Candidate` predicates follow the same shape: class/type declaration with a base list and not `abstract`.

### Handler pipeline — `GetHandlerInfo`

Walks `classSymbol.AllInterfaces`, matches `originalDefinition.ToDisplayString()` against the fully qualified open interface name, and captures type arguments. `[MediatorGeneration(Skip = true)]` short-circuits discovery. `[NotificationHandlerOrder]` is read here (shared across all notification handler interfaces on the class).

```347:416:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

The `HasDataAnnotations` flag here is what later drives the automatic `DataAnnotationsValidator<TRequest>` registration.

### Behavior pipeline — `GetBehaviorInfo`

Captures every `IPipelineBehavior<TRequest, TResponse>` interface on the class; detects whether the interface arguments are open type parameters and stores that as `IsOpenGeneric`. Reads `[BehaviorOrder(int)]` for deterministic ordering during pipeline emission.

```241:256:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

### Validator pipeline — `GetValidatorInfo`

Only **closed** validators are discovered — open generic validators (such as the library's `DataAnnotationsValidator<T>`) are explicitly excluded so they are not double-registered.

```265:311:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

### Notification pipeline — `GetNotificationInfo`

Unlike the other three pipelines, this one discovers **notification type declarations** (not handlers) to capture per-type `[NotificationExecution]` / `[NotificationError]` attributes. If neither attribute is present, the pipeline returns `null` and the type falls back to assembly + library defaults.

```144:186:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

### Assembly defaults — `GetAssemblyDefaults`

Reads the `[assembly: ...]` attributes in a single pass and captures four signals at once: execution strategy default, error strategy default, logging-disabled flag, and tracing-disabled flag.

```82:115:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

### Compile-time strategy resolution — `ResolveStrategies`

Documents the precedence contract: per-notification attribute → assembly default → library default. Library defaults are `Sequential` for execution and `StopOnFirstError` for error (enum value `0` for both).

```117:129:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

### `Execute` — glue that builds everything

Determines which requests need validation (from `HasDataAnnotations` or a discovered custom `IValidator`), expands open generic behaviors into closed types per request/response pair, and appends `ValidationBehavior<TReq, TRes>` entries ahead of all other behaviors.

```418:455:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

### Emitted `MediatorLiteRegistration` — the public surface

The generated class lives in `MediatorLite.Generated` and exposes one all-in-one method plus four granular methods, each returning `IServiceCollection`:

```643:657:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

Diagnostic counts (useful for sanity checks in tests and startup logs):

```790:801:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

When **no** handlers are discovered, `GenerateEmptyRegistration` emits a stub with all methods returning `services` unchanged and all counts equal to `0` — consumers can still safely call `AddGeneratedHandlers()` without an NRE.

```538:606:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

### Emitted `SourceGeneratedMediator` — dispatch tables + unrolled pipelines

`SourceGeneratedMediator` is registered as **singleton** (line 653 above); it contains two static `Dictionary<Type, RequestDispatcher>` / `Dictionary<Type, NotificationPublisher>` fields that map runtime types to static lambdas invoking the unrolled per-request methods:

```859:884:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
        sb.AppendLine("    public sealed class SourceGeneratedMediator : global::MediatorLite.ISourceGeneratedMediator");
        sb.AppendLine("    {");

        // === DISPATCH DICTIONARY ===
        sb.AppendLine("        // O(1) request dispatch table");
        sb.AppendLine("        private static readonly Dictionary<Type, global::MediatorLite.RequestDispatcher> _dispatchers = new()");
        sb.AppendLine("        {");
        foreach (var (handler, iface) in requestHandlers)
        {
            var safeName = GetSafeTypeName(iface.RequestType);
            sb.AppendLine($"            [typeof({iface.RequestType})] = static (sp, req, ct) => Pipeline_{safeName}(sp, ({iface.RequestType})req, ct),");
        }
```

Each request gets a private static `Pipeline_<SafeRequestName>` method. With zero behaviors the emitted body is a direct `handler.HandleAsync(...)` call; with behaviors the generator emits a **nested delegate chain** from outside-in:

```986:1025:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
        void EmitPipelineBody(string indent)
        {
            if (behaviors.Count == 0)
            {
                // Zero-behavior fast path — direct handler call
                sb.AppendLine($"{indent}var handler = sp.GetRequiredService<global::MediatorLite.IRequestHandler<{requestType}, {responseType}>>();");
                sb.AppendLine($"{indent}var result = await handler.HandleAsync(request, ct).ConfigureAwait(false);");
            }
            else
            {
                // Resolve all behaviors by concrete type and handler
                for (int i = 0; i < behaviors.Count; i++)
                {
                    var behavior = behaviors[i];
                    sb.AppendLine($"{indent}var b{i + 1} = sp.GetRequiredService<{behavior.BehaviorTypeName}>();");
                }
                sb.AppendLine($"{indent}var handler = sp.GetRequiredService<global::MediatorLite.IRequestHandler<{requestType}, {responseType}>>();");
                sb.AppendLine();

                // Build the nested delegate chain from outside in
                sb.Append($"{indent}var result = await ");
                for (int i = 0; i < behaviors.Count; i++)
                {
                    sb.Append($"b{i + 1}.HandleAsync(request, () => ");
                }
                sb.Append("handler.HandleAsync(request, ct)");
                for (int i = behaviors.Count - 1; i >= 0; i--)
                {
                    sb.Append(", ct)");
                }
                sb.AppendLine(".ConfigureAwait(false);");
            }
```

### Inline logging + tracing emission

When observability is enabled (default), the pipeline body is wrapped in `try / catch (Exception __ex)` with `LogDebug` / `ActivitySource.StartActivity` calls inline. Constants for activity names are hardcoded in the generator (since the generator targets `netstandard2.0` and cannot reference the runtime):

```21:26:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
    // Mirrors constants in src/MediatorLite/Diagnostics/MediatorDiagnostics.cs.
    // Kept in sync manually because the generator project (netstandard2.0) cannot
    // reference the runtime MediatorLite assembly. If the runtime constants change,
    // update these literals to match.
    private const string ActivityNameSendRequest = "MediatorLite.Send";
    private const string ActivityNamePublishNotification = "MediatorLite.Publish";
```

The observability emission is a branch inside `GenerateUnrolledPipeline`:

```1032:1073:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
        bool needsDiagnostics = loggingEnabled || tracingEnabled;

        if (needsDiagnostics)
        {
            if (loggingEnabled)
            {
                sb.AppendLine("            var __logger = sp.GetRequiredService<global::Microsoft.Extensions.Logging.ILogger<global::MediatorLite.IMediator>>();");
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
            EmitPipelineBody("                ");
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
        }
        else
        {
            // Fully-disabled fast path — no try/catch, no locals
            EmitPipelineBody("            ");
        }
```

### Notification publisher emission

`GenerateUnrolledNotificationPublisher` picks **exactly one** of `GenerateSequentialNotificationExecution`, `GenerateParallelNotificationExecution`, or `GenerateStopOnFirstNotificationExecution` based on the resolved strategy tuple. The emitted body is therefore branch-free at runtime — no switch on strategy, no runtime enum check. See the `ResolveStrategies` call:

```933:955:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
        var notificationsByType = notifications
            .GroupBy(n => n.TypeName)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var notifGroup in handlersByNotification)
        {
            var notificationType = notifGroup.Key;
            var safeName = GetSafeTypeName(notificationType);
            var handlersForNotification = notifGroup.Value;

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
```

### ValidationBehavior is emitted first, by design

The emission for `AddGeneratedBehaviors` explicitly registers `ValidationBehavior<,>` entries before any other behavior so they run before user-ordered behaviors:

```752:762:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

### DataAnnotationsValidator auto-registration

Request types whose `HasDataAnnotations` flag is set (any property has a `[ValidationAttribute]`-derived attribute) are registered with the library's open generic `DataAnnotationsValidator<T>`:

```723:736:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

`HasDataAnnotationAttributes` walks all properties and their attributes, climbing the inheritance chain to detect `System.ComponentModel.DataAnnotations.ValidationAttribute`:

```316:345:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
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
```

### Emitted file structure summary

Two generated files per compilation with handlers:

- **`MediatorLiteRegistration.g.cs`** — `namespace MediatorLite.Generated { static class MediatorLiteRegistration { AddGeneratedHandlers, AddGeneratedRequestHandlers, AddGeneratedNotificationHandlers, AddGeneratedValidators, AddGeneratedBehaviors, RequestHandlerCount, NotificationHandlerCount, BehaviorCount, ValidatorCount } }`
- **`SourceGeneratedMediator.g.cs`** — `namespace MediatorLite.Generated { sealed class SourceGeneratedMediator : ISourceGeneratedMediator { _dispatchers, _publishers, GetDispatcher, GetPublisher, Pipeline_<Type>..., Publish_<Type>... } }`

## Patterns & invariants

**Do:**
- Keep any new constant that the generator emits in sync with the runtime equivalent (the generator cannot reference `MediatorLite.dll`; see the comment above `ActivityNameSendRequest`).
- Emit `static` lambdas for dispatch table entries to avoid closure allocations.
- Emit `[MethodImpl(MethodImplOptions.AggressiveInlining)]` on `GetDispatcher`/`GetPublisher`.
- Register handlers, behaviors, and validators with `AddTransient`. `SourceGeneratedMediator` itself is registered `AddSingleton` because its state is static `Dictionary`s.
- Register each behavior by its **concrete type** (not only by `IPipelineBehavior<,>`) so the unrolled pipeline can `GetRequiredService<T>()` it individually.
- Resolve strategies via `ResolveStrategies` (precedence: per-type → assembly → library default `0`).

**Don't:**
- Don't emit reflection, `Activator.CreateInstance`, or late-bound dispatch — the whole point is compile-time elimination.
- Don't discover **open generic** validators — they are explicitly skipped (`classSymbol.IsGenericType` in `GetValidatorInfo`).
- Don't add runtime switches on `NotificationExecutionStrategy` — the emitted body must have exactly one code path.
- Don't reference the `MediatorLite` runtime from this project (it targets `netstandard2.0`).

## Common tasks

1. **Inspect the generated code**
   1. In the consumer `.csproj` add:
      ```
      <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
      <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\generated</CompilerGeneratedFilesOutputPath>
      ```
   2. Build; the generated files land in `obj/Debug/netX/generated/MediatorLite.SourceGeneration/MediatorLite.SourceGeneration.HandlerDiscoveryGenerator/`.
   3. Alternatively, check `MediatorLiteRegistration.RequestHandlerCount` at startup — `0` indicates the generator did not discover any handlers in the compilation.

2. **Add a new candidate kind (e.g. a new `IMyHandler<T>`)**
   1. Add an `Is<Kind>Candidate` predicate and `Get<Kind>Info` transform in the generator, paralleling `IsHandlerCandidate` / `GetHandlerInfo`.
   2. Add a new pipeline in `Initialize` and combine it into the `compilationAndData` tuple.
   3. Update `Execute` to call a new `Generate<Kind>...` emitter.
   4. Update `MediatorLiteRegistration` to expose an `AddGenerated<Kind>` method and a `<Kind>Count` constant.

3. **Change how behaviors compose**
   1. Edit `ExpandBehaviors` for how open generic behaviors produce closed types per (request, response) pair.
   2. Edit `GenerateUnrolledPipeline.EmitPipelineBody` for the actual emission — note the nested delegate shape at lines 1005-1017.

4. **Change compile-time strategy precedence**
   1. Edit `ResolveStrategies` only — it is the single source of truth. `GenerateUnrolledNotificationPublisher` already receives the resolved tuple.

5. **Debug "no handler found" in consumer**
   1. Confirm the class is `public` + non-`abstract` + implements the open interface definition (fully qualified).
   2. Confirm the consumer project adds the analyzer reference with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`.
   3. Confirm the handler is not decorated with `[MediatorGeneration(Skip = true)]`.
   4. Check `obj/.../SourceGeneratedMediator.g.cs` for the `Pipeline_<Type>` method.

## Pitfalls & gotchas

- **`[MediatorGeneration(Skip = true)]` is silently honored** in `GetHandlerInfo`, `GetBehaviorInfo`, and `GetValidatorInfo`. The attribute is marked `[Obsolete]` in `Attributes.cs` but the generator still respects it — remove any usage during migrations.
- **Validator open generics are excluded**. If you implement `MyValidator<T> : IValidator<T>`, it will **not** be auto-registered. Register it manually or make it closed.
- **`HasDataAnnotationAttributes` only scans direct property attributes**, not fields or nested object properties. If you need nested validation, wire a custom `IValidator<T>`.
- **Dispatcher keys use `typeof(requestType)` with the runtime type from `Mediator.SendAsync`** — abstract/base class requests are **not** registered, only closed concrete types implementing `IRequest<TResponse>` (for which a handler exists).
- **String-based attribute matching**: the generator compares `attr.AttributeClass?.Name` against literals like `"NotificationExecutionAttribute"`. Renaming the attribute types in `Attributes.cs` **breaks the generator**.
- **Abstract classes are filtered out** (`classSymbol.IsAbstract`). Keep handlers concrete.
- **Generator targets `netstandard2.0`**. You cannot use collection expressions, ranges, `ArgumentNullException.ThrowIfNull`, etc., in generator code — use explicit C# 8-compatible syntax.
- **`ActivityNameSendRequest` / `ActivityNamePublishNotification`** must be kept in sync with `MediatorActivitySource.ActivityNames.*` in [MediatorDiagnostics.cs](src/MediatorLite/Diagnostics/MediatorDiagnostics.cs).
- **Logging category hardcoded**: emitted code uses `ILogger<global::MediatorLite.IMediator>` — the filter key is always `MediatorLite.IMediator`.

## Related skills & rules

- **mediatorlite-abstractions** — source of the interfaces (`IRequestHandler<,>`, `INotificationHandler<>`, `IPipelineBehavior<,>`, `IValidator<T>`, `INotification`) and attributes (`BehaviorOrderAttribute`, `NotificationExecutionAttribute`, `NotificationErrorAttribute`, `DefaultNotificationExecutionAttribute`, `DefaultNotificationErrorAttribute`, `DisableMediatorLoggingAttribute`, `DisableMediatorTracingAttribute`, `MediatorGenerationAttribute`) that this generator reads.
- **mediatorlite-core** — consumes the generated `ISourceGeneratedMediator`; `ValidationBehavior` and `DataAnnotationsValidator` are runtime types this generator references by fully-qualified name.
- **mediatorlite-tests** — `tests/MediatorLite.Tests/SourceGeneration/*` tests verify the generated dispatch, pipeline composition, notification strategies, and `*Count` properties.
- **mediatorlite-sample-sourcegen** — canonical consumer wiring demonstrating `AddGeneratedHandlers` + `AddMediatorLite`.
- [AGENTS.md](AGENTS.md): "Source-generation entry point is `HandlerDiscoveryGenerator.cs`; generated diagnostics surface as `MediatorLite.Generated.MediatorLiteRegistration`".
- Docs: [docs/notifications.md](docs/notifications.md), [docs/observability.md](docs/observability.md), [src/MediatorLite.SourceGeneration/README.md](src/MediatorLite.SourceGeneration/README.md).
