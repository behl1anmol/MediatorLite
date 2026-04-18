---
name: mediatorlite-core
description: Runtime implementation for MediatorLite -- Mediator.cs internals, AddMediatorLite() DI extension, PipelineBehaviorTypeResolver, NullSourceGeneratedMediator fallback, MediatorDiagnostics (ActivitySource + DiagnosticListener), and the Validation subsystem (ValidationBehavior + DataAnnotationsValidator). Use when touching dispatch, DI registration, diagnostics, or the validation runtime.
triggers: Mediator.cs, AddMediatorLite, dispatch, ISourceGeneratedMediator runtime, ServiceCollectionExtensions, PipelineBehaviorTypeResolver, MediatorDiagnostics, MediatorActivitySource, NullSourceGeneratedMediator, ValidationBehavior, DataAnnotationsValidator, MediatorLite runtime
---

# MediatorLite (Core Runtime)

## Purpose

`MediatorLite` (project name, not the solution) is the runtime library consumers reference. It contains **only** the `IMediator` implementation, DI extension, helper type resolvers, diagnostic sources, and the validation runtime. The dispatch path contains **zero reflection** at call time — the compile-time `ISourceGeneratedMediator` emitted by `MediatorLite.SourceGeneration` owns all type→handler lookup tables, and this project just routes calls through it. Logging and tracing are emitted inline by the generator, not by this project.

## When to use

- Changing how `IMediator.SendAsync` / `PublishAsync` routes to the source-generated dispatcher.
- Adding or tweaking `AddMediatorLite()` registrations (for example, swapping lifetimes or registering additional runtime services).
- Adjusting `ValidationBehavior` ordering semantics or `DataAnnotationsValidator` behavior.
- Fixing a bug in `PipelineBehaviorTypeResolver` when wiring closed vs open generic behaviors.
- Renaming or adding OpenTelemetry tags / activity names in `MediatorDiagnostics`.

## Project location & entry points

- [MediatorLite.csproj](src/MediatorLite/MediatorLite.csproj) — targets `net10.0`, references `Microsoft.Extensions.DependencyInjection.Abstractions 9.0.0` and `Microsoft.Extensions.Logging.Abstractions 9.0.0`, and project-references [MediatorLite.Abstractions.csproj](src/MediatorLite.Abstractions/MediatorLite.Abstractions.csproj).
- [Mediator.cs](src/MediatorLite/Internal/Mediator.cs) — the `IMediator` implementation.
- [ServiceCollectionExtensions.cs](src/MediatorLite/Configuration/ServiceCollectionExtensions.cs) — `AddMediatorLite()` entry point.
- [NullSourceGeneratedMediator.cs](src/MediatorLite/Internal/NullSourceGeneratedMediator.cs) — fallback `ISourceGeneratedMediator`.
- [PipelineBehaviorTypeResolver.cs](src/MediatorLite/Configuration/PipelineBehaviorTypeResolver.cs) — helper used by the source generator and by any manual DI registration.
- [MediatorDiagnostics.cs](src/MediatorLite/Diagnostics/MediatorDiagnostics.cs) — `MediatorActivitySource` (OpenTelemetry) + `DiagnosticListener`.
- [ValidationBehavior.cs](src/MediatorLite/Validation/ValidationBehavior.cs) — generic pipeline behavior that runs registered `IValidator<T>`s.
- [DataAnnotationsValidator.cs](src/MediatorLite/Validation/DataAnnotationsValidator.cs) — built-in validator using `System.ComponentModel.DataAnnotations`.

## Core types / API surface

### `Mediator` — the O(1) dispatcher

```21:32:src/MediatorLite/Internal/Mediator.cs
internal sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISourceGeneratedMediator _sourceGeneratedMediator;

    public Mediator(
        IServiceProvider serviceProvider,
        ISourceGeneratedMediator sourceGeneratedMediator)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _sourceGeneratedMediator = sourceGeneratedMediator ?? throw new ArgumentNullException(nameof(sourceGeneratedMediator));
    }
```

`SendAsync` looks up the generated `RequestDispatcher`, invokes it, and casts the `object` result back to `TResponse` (the boxing tradeoff documented in the abstractions skill):

```34:50:src/MediatorLite/Internal/Mediator.cs
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var dispatcher = _sourceGeneratedMediator.GetDispatcher(requestType)
            ?? throw new InvalidOperationException(
                $"No handler registered for request type {requestType.FullName}. " +
                $"Ensure a handler implementing IRequestHandler<{requestType.Name}, {typeof(TResponse).Name}> " +
                "is registered and AddGeneratedHandlers() is called.");

        var result = await dispatcher(_serviceProvider, request, cancellationToken).ConfigureAwait(false);
        return (TResponse)result;
    }
```

`PublishAsync` silently returns `Task.CompletedTask` when no handler is registered for a notification — the test [PublishAsync_WithNoHandlers_CompletesWithoutError](tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs) verifies this:

```52:64:src/MediatorLite/Internal/Mediator.cs
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        var publisher = _sourceGeneratedMediator.GetPublisher(typeof(TNotification));
        return publisher is null
            ? Task.CompletedTask
            : publisher(_serviceProvider, notification, cancellationToken);
    }
```

Key invariants:
- `Mediator` is marked `[MethodImpl(AggressiveInlining)]` on both hot methods.
- The dispatcher returns `Task<object>`; the final cast unboxes value-type responses (including `Unit`).
- The lookup uses `request.GetType()` — **not** `typeof(TRequest)` — so covariant/derived runtime types dispatch correctly even when the caller declares `IRequest<TResponse>`.

### `AddMediatorLite()` — DI entry point

```37:41:src/MediatorLite/Configuration/ServiceCollectionExtensions.cs
    public static IServiceCollection AddMediatorLite(this IServiceCollection services)
    {
        services.AddTransient<IMediator, Mediator>();
        return services;
    }
```

**Transient** is intentional:
- `Mediator` is stateless; holding it as singleton adds no benefit.
- Transient matches the conventional lifetime pattern in `Microsoft.Extensions.DependencyInjection` for lightweight dispatchers.
- The scoped `IServiceProvider` injected into `Mediator` is still used for handler resolution, so transient does not prevent scoped handler resolution.
- See the workspace rule in [AGENTS.md](AGENTS.md): "the mediator is always registered as `Transient`".

`AddMediatorLite()` takes **no arguments**. There is no `MediatorOptions` — v2 removed [src/MediatorLite/Configuration/MediatorOptions.cs](src/MediatorLite/Configuration/MediatorOptions.cs) (see git status). All configuration is compile-time via attributes.

### `NullSourceGeneratedMediator` — fallback sentinel

```8:17:src/MediatorLite/Internal/NullSourceGeneratedMediator.cs
internal sealed class NullSourceGeneratedMediator : ISourceGeneratedMediator
{
    public static readonly NullSourceGeneratedMediator Instance = new();

    private NullSourceGeneratedMediator() { }

    public RequestDispatcher? GetDispatcher(Type requestType) => null;

    public NotificationPublisher? GetPublisher(Type notificationType) => null;
}
```

This type is used in two places:
1. Tests that want to exercise `Mediator` without any generated handlers (the singleton `Instance`).
2. The source-generated `AddGeneratedHandlers` **replaces** any prior `ISourceGeneratedMediator` binding — if no generator runs, resolving `Mediator` will throw the `InvalidOperationException` shown above.

### `PipelineBehaviorTypeResolver` — behavior interface discovery

This helper distinguishes open vs closed behavior types. The source generator uses it when emitting `services.Add*` lines, and manual test code can call it to validate a registration.

```3:18:src/MediatorLite/Configuration/PipelineBehaviorTypeResolver.cs
internal static class PipelineBehaviorTypeResolver
{
    private static readonly Type OpenPipelineBehaviorType = typeof(IPipelineBehavior<,>);

    internal static IReadOnlyList<Type> GetServiceTypesForRegistration(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (behaviorType.IsGenericTypeDefinition)
        {
            ValidateOpenBehaviorType(behaviorType);
            return [OpenPipelineBehaviorType];
        }

        return GetClosedBehaviorInterfacesOrThrow(behaviorType);
    }
```

Closed behaviors (e.g. `PlaceOrderAuthorizationBehavior : IPipelineBehavior<PlaceOrderCommand, Unit>`) yield the exact closed interface; open behaviors (`LoggingBehavior<TRequest, TResponse>`) yield the unbound `IPipelineBehavior<,>`.

```67:93:src/MediatorLite/Configuration/PipelineBehaviorTypeResolver.cs
    internal static void ValidateOpenBehaviorType(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (!behaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"Type {behaviorType.Name} must be an open generic type definition.",
                "behaviorType");
        }

        var hasCorrectArity = behaviorType.GetGenericArguments().Length == 2;
        if (!hasCorrectArity)
        {
            throw new ArgumentException(
                $"Type {behaviorType.Name} must have exactly 2 generic type parameters.",
                "behaviorType");
        }

        var implementsPipelineBehavior = behaviorType.GetInterfaces()
            .Any(IsPipelineBehaviorInterface)
            .(continued)
```

`GetClosedBehaviorInterfaceForInvocation` is used if you need to build a runtime pipeline manually (e.g. in a fallback scenario): it resolves the specific `IPipelineBehavior<TRequest, TResponse>` implementation of a closed type.

### `MediatorActivitySource` + `MediatorDiagnostics`

The generator emits `Activity?` starts with these constants; consumers subscribe via OpenTelemetry.

```8:23:src/MediatorLite/Diagnostics/MediatorDiagnostics.cs
public static class MediatorActivitySource
{
    /// <summary>
    /// The name of the activity source.
    /// </summary>
    public const string SourceName = "MediatorLite";

    /// <summary>
    /// The version of the activity source.
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// The ActivitySource for MediatorLite tracing.
    /// </summary>
    public static readonly ActivitySource Source = new(SourceName, Version);
```

```28:41:src/MediatorLite/Diagnostics/MediatorDiagnostics.cs
    public static class ActivityNames
    {
        /// <summary>Send request activity name prefix.</summary>
        public const string SendRequest = "MediatorLite.Send";

        /// <summary>Publish notification activity name prefix.</summary>
        public const string PublishNotification = "MediatorLite.Publish";

        /// <summary>Pipeline behavior activity name prefix.</summary>
        public const string PipelineBehavior = "MediatorLite.Behavior";

        /// <summary>Notification handler activity name prefix.</summary>
        public const string NotificationHandler = "MediatorLite.NotificationHandler";
    }
```

Standard tag names are also centralized here:

```46:74:src/MediatorLite/Diagnostics/MediatorDiagnostics.cs
    public static class Tags
    {
        /// <summary>Request type tag.</summary>
        public const string RequestType = "mediatorlite.request.type";

        /// <summary>Response type tag.</summary>
        public const string ResponseType = "mediatorlite.response.type";

        /// <summary>Notification type tag.</summary>
        public const string NotificationType = "mediatorlite.notification.type";

        /// <summary>Handler type tag.</summary>
        public const string HandlerType = "mediatorlite.handler.type";

        /// <summary>Behavior type tag.</summary>
        public const string BehaviorType = "mediatorlite.behavior.type";

        /// <summary>Handler count tag.</summary>
        public const string HandlerCount = "mediatorlite.handler.count";

        /// <summary>Execution strategy tag.</summary>
        public const string ExecutionStrategy = "mediatorlite.execution.strategy";

        /// <summary>Error tag.</summary>
        public const string Error = "error";

        /// <summary>Error message tag.</summary>
        public const string ErrorMessage = "error.message";
    }
```

`MediatorDiagnostics.Listener` exposes a `DiagnosticListener` named `"MediatorLite"` with event constants under `MediatorDiagnostics.Events` for optional `DiagnosticSource`-based integration.

### `ValidationBehavior<TRequest, TResponse>`

Registered automatically by the generator for any request type that has at least one `IValidator<TRequest>` (including `DataAnnotationsValidator<TRequest>` when the request class has any `[ValidationAttribute]`). It is inserted **before** other behaviors in the generated pipeline.

```10:22:src/MediatorLite/Validation/ValidationBehavior.cs
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private IReadOnlyList<IValidator<TRequest>> Validators { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="validators">The validators for the request type.</param>
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        Validators = validators.ToList();
    }
```

Execution runs every registered validator and **aggregates** their errors before throwing `ValidationException` — it does not stop at the first failed validator:

```25:53:src/MediatorLite/Validation/ValidationBehavior.cs
    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        if (Validators.Count == 0)
        {
            return await next();
        }

        var allErrors = new List<ValidationError>();

        foreach (var validator in Validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            if (!result.IsValid)
            {
                allErrors.AddRange(result.Errors);
            }
        }

        if (allErrors.Count > 0)
        {
            throw new ValidationException(allErrors);
        }

        return await next();
    }
```

### `DataAnnotationsValidator<TRequest>`

Registered by the generator only for request types that carry any `System.ComponentModel.DataAnnotations` attribute. `null` requests short-circuit to a failure with a `"Request"` property name.

```11:35:src/MediatorLite/Validation/DataAnnotationsValidator.cs
public class DataAnnotationsValidator<TRequest> : IValidator<TRequest>
{
    /// <inheritdoc />
    public ValueTask<ValidationResult> ValidateAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return ValueTask.FromResult(ValidationResult.Failure(
                new ValidationError("Request", "Request cannot be null")));
        }

        var context = new ValidationContext(request);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        if (Validator.TryValidateObject(request, context, results, validateAllProperties: true))
        {
            return ValueTask.FromResult(ValidationResult.Success);
        }

        var errors = results.Select(r => new ValidationError(
            string.Join(", ", r.MemberNames),
            r.ErrorMessage ?? "Validation failed")).ToList();

        return ValueTask.FromResult(ValidationResult.Failure(errors));
    }
```

## Patterns & invariants

**Do:**
- Call `services.AddGeneratedHandlers().AddMediatorLite()` in that order. `AddGeneratedHandlers()` is provided by the generator and registers `ISourceGeneratedMediator` + all handlers/behaviors/validators; `AddMediatorLite()` only adds the `IMediator` facade.
- Keep `Mediator.cs` small. All the heavy lifting (dispatch tables, pipeline composition, logging, tracing) is emitted by the generator.
- Let `ValidationBehavior` aggregate errors across validators — that is intentional.
- Use `MediatorActivitySource.Source` as the subscription target in OpenTelemetry setup (`builder.AddSource("MediatorLite")`).

**Don't:**
- Don't add reflection-based dispatch. v2 deliberately removed it; see the `Mediator.cs` remarks.
- Don't register `IMediator` manually — always go through `AddMediatorLite()`.
- Don't change `Mediator` to `Scoped` or `Singleton` without coordinating with generated code. The injected `IServiceProvider` is the scope root; the mediator being transient avoids any captive-dependency issues.
- Don't throw on `PublishAsync` when there are no handlers. Returning `Task.CompletedTask` is a behavioral contract verified in tests.
- Don't introduce a `MediatorOptions` class. The file was deliberately deleted.

## Common tasks

1. **Diagnose "No handler registered for request type X"**
   1. Confirm the consumer calls `services.AddGeneratedHandlers()` **before** or alongside `AddMediatorLite()`.
   2. Look at the obj/generated folder (`obj/Debug/netX/generated/MediatorLite.SourceGeneration/.../MediatorLiteRegistration.g.cs`) for the handler's dispatch entry.
   3. Check `MediatorLiteRegistration.RequestHandlerCount` at startup — a `0` means the generator never discovered anything (likely because the handler is `abstract`, `internal` in a different assembly, or has `[MediatorGeneration(Skip = true)]`).

2. **Wire OpenTelemetry tracing**
   1. In the consumer: `builder.Services.AddOpenTelemetry().WithTracing(t => t.AddSource(MediatorActivitySource.SourceName))`.
   2. Tags emitted by the generator use the `MediatorActivitySource.Tags.*` constants — consumer dashboards can filter on `mediatorlite.request.type`, `mediatorlite.handler.type`, etc.
   3. Opt out per assembly with `[assembly: DisableMediatorTracing]`.

3. **Adjust logger category / log level**
   1. The generator always emits `LogDebug` under the category `MediatorLite.IMediator`. To quiet it, add `AddFilter("MediatorLite.IMediator", LogLevel.Information)` (or higher) to the logging configuration.
   2. To remove the calls entirely, use `[assembly: DisableMediatorLogging]`.

4. **Register a custom `IValidator<T>` without source generation**
   1. `services.AddTransient<IValidator<MyCommand>, MyValidator>();`
   2. Also register `ValidationBehavior<,>` for the request — normally the generator does this, but if you're mixing manual DI it must live in `IPipelineBehavior<MyCommand, TResponse>`.

5. **Unit-test the dispatch path without the generator**
   1. Build the mediator directly: `var mediator = new Mediator(serviceProvider, NullSourceGeneratedMediator.Instance);`
   2. `SendAsync` will throw `InvalidOperationException` — useful to verify error messaging.
   3. For positive tests, always let the test project's source generator run (it is already wired — see [tests/MediatorLite.Tests/MediatorLite.Tests.csproj](tests/MediatorLite.Tests/MediatorLite.Tests.csproj)).

## Pitfalls & gotchas

- **`request.GetType()` vs `typeof(TRequest)`**: dispatch uses the runtime type, which matters if callers declare the parameter as `IRequest<T>`. Test `MediatorTests` uses `mediator.SendAsync(new Ping("hi"))` — the generic is inferred from the record and then `GetType()` resolves the closed dispatcher.
- **Null requests on `PublishAsync`** throw `ArgumentNullException`, **not** `ArgumentException`. Tests in [MediatorTests.cs](tests/MediatorLite.Tests/SourceGeneration/MediatorTests.cs) assert this precisely.
- **`PipelineBehaviorTypeResolver.ValidateOpenBehaviorType` throws `ArgumentException("Type X must be an open generic...")`** — note the literal `"behaviorType"` parameter name (no `nameof` is used), so downstream consumers that catch on the parameter name should use the string literal.
- **`MediatorOptions.cs` is deleted** (see git status `D src/MediatorLite/Configuration/MediatorOptions.cs`). Do not re-introduce it.
- **`ValidationBehavior.Validators` is stored once** in the constructor (`validators.ToList()`). If a scoped container registers different validators per scope, a transient mediator still re-resolves per scope — this works correctly only because `ValidationBehavior` itself is transient via DI.
- **`ValidationException` is thrown inside `await validator.ValidateAsync(...)`**; it does **not** wrap individual validator errors — all errors flatten into one exception.
- **`DiagnosticListener Listener = new("MediatorLite")`** is publicly visible — do not rename its source without coordinating with downstream diagnostic observers.

## Related skills & rules

- **mediatorlite-abstractions** — defines `IMediator`, `IRequest`, `IPipelineBehavior`, `ISourceGeneratedMediator`, and the `IValidator<T>` / validation model types consumed here.
- **mediatorlite-source-generation** — emits the `ISourceGeneratedMediator` implementation that this project routes through, plus `AddGeneratedHandlers` that registers everything before `AddMediatorLite`.
- **mediatorlite-tests** — `tests/MediatorLite.Tests/SourceGeneration/MediatorTests.cs`, `ValidationTests.cs`, `PipelineBehaviorTests.cs`, and `NotificationTests.cs` exercise `Mediator.cs`, `ValidationBehavior`, and the DI wiring.
- [AGENTS.md](AGENTS.md) — "The mediator is always registered as `Transient`. `Mediator.cs` depends on `ISourceGeneratedMediator`; do not rely on reflection fallback".
- Docs: [docs/observability.md](docs/observability.md), [docs/validation.md](docs/validation.md), [docs/pipeline-behaviors.md](docs/pipeline-behaviors.md).
