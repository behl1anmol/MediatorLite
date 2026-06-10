---
name: mediatorlite-core
description: Runtime implementation for MediatorLite -- AddMediatorLite() DI extension, the ThrowingMediator diagnostic fallback, the generated SourceGeneratedMediator (implements IMediator via ValueTask typed-switch dispatch), MediatorDiagnostics (ActivitySource + DiagnosticListener), and the Validation subsystem (ValidationBehavior + DataAnnotationsValidator). Use when touching dispatch, DI registration, diagnostics, or the validation runtime.
triggers: AddMediatorLite, dispatch, SourceGeneratedMediator, ThrowingMediator, ValueTask dispatch, ServiceCollectionExtensions, MediatorDiagnostics, MediatorActivitySource, ValidationBehavior, DataAnnotationsValidator, MediatorLite runtime
---

# MediatorLite (Core Runtime)

## Purpose

`MediatorLite` (project name, not the solution) is the runtime library consumers reference. It does **not** contain a hand-written `IMediator` implementation — the real `IMediator` is the generated `SourceGeneratedMediator` emitted by `MediatorLite.SourceGeneration`. This project contains the DI extension (`AddMediatorLite()`), the `ThrowingMediator` diagnostic fallback, diagnostic sources, and the validation runtime. The dispatch path contains **zero reflection** at call time — the generated mediator dispatches via a compile-time C# type-pattern switch. Logging and tracing are emitted inline by the generator, not by this project.

## When to use

- Adding or tweaking `AddMediatorLite()` registrations (for example, registering additional runtime services).
- Understanding the `ThrowingMediator` diagnostic fallback and how the generated mediator supersedes it.
- Adjusting `ValidationBehavior` ordering semantics or `DataAnnotationsValidator` behavior.
- Renaming or adding OpenTelemetry tags / activity names in `MediatorDiagnostics`.

## Project location & entry points

- [MediatorLite.csproj](src/MediatorLite/MediatorLite.csproj) — targets `net10.0`, references `Microsoft.Extensions.DependencyInjection.Abstractions 9.0.0` and `Microsoft.Extensions.Logging.Abstractions 9.0.0`, and project-references [MediatorLite.Abstractions.csproj](src/MediatorLite.Abstractions/MediatorLite.Abstractions.csproj).
- The `IMediator` implementation is **generated** (`SourceGeneratedMediator` in the `MediatorLite.Generated` namespace) — it is not a file in this project. See the mediatorlite-source-generation skill.
- [ServiceCollectionExtensions.cs](src/MediatorLite/Configuration/ServiceCollectionExtensions.cs) — `AddMediatorLite()` entry point.
- [ThrowingMediator.cs](src/MediatorLite/Internal/ThrowingMediator.cs) — diagnostic fallback `IMediator` that throws if no generator ran.
- ~~`PipelineBehaviorTypeResolver.cs`~~ — **deleted** (v1 runtime behavior-type resolution; the generated mediator unrolls behaviors at compile time, so nothing needs it).
- [MediatorDiagnostics.cs](src/MediatorLite/Diagnostics/MediatorDiagnostics.cs) — `MediatorActivitySource` (OpenTelemetry) + `DiagnosticListener`.
- [ValidationBehavior.cs](src/MediatorLite/Validation/ValidationBehavior.cs) — generic pipeline behavior that runs registered `IValidator<T>`s.
- [DataAnnotationsValidator.cs](src/MediatorLite/Validation/DataAnnotationsValidator.cs) — built-in validator using `System.ComponentModel.DataAnnotations`.

## Core types / API surface

### The generated `SourceGeneratedMediator` — typed-switch dispatch

There is **no** hand-written `Mediator.cs` in v2. The generator emits `SourceGeneratedMediator : global::MediatorLite.IMediator` (namespace `MediatorLite.Generated`) which holds a single `IServiceProvider _sp` field and dispatches via a compile-time C# type-pattern switch:

```csharp
// Emitted shape (MediatorLite.Generated.SourceGeneratedMediator)
public sealed class SourceGeneratedMediator : global::MediatorLite.IMediator
{
    private readonly IServiceProvider _sp;
    public SourceGeneratedMediator(IServiceProvider serviceProvider) => _sp = serviceProvider;

    public ValueTask<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        switch (request)
        {
            case MyQuery r:
            {
                var vt = Send_MyQuery(r, ct);                  // ValueTask<MyResult>
                if (typeof(TResponse) == typeof(MyResult))
                    return Unsafe.As<ValueTask<MyResult>, ValueTask<TResponse>>(ref vt);
                return SlowCast<MyResult, TResponse>(vt);      // covariant IRequest<out T> fallback
            }
            case null: throw new ArgumentNullException(nameof(request));
            default:   throw new InvalidOperationException(/* no handler */);
        }
    }
    // ...PublishAsync switch + Send_<Type>/Publish_<Type> methods using _sp...
}
```

Key invariants:
- **No boxing.** Each arm calls a fully typed `Send_<SafeType>(...)` returning `ValueTask<TConcrete>`, converted to `ValueTask<TResponse>` via an identity-guarded `System.Runtime.CompilerServices.Unsafe.As` (the `typeof` guard JIT-folds to a constant). Value-type responses stay typed — there is no `Task<object>` and no `(TResponse)` unbox. v1 boxed; **v2 eliminated it.**
- **`SlowCast`** is the only fallback, for covariant `IRequest<out T>` dispatch (reference cast, no value-type boxing).
- **`Send_<SafeType>` per-request methods** are instance methods on `_sp`. A zero-behavior request with diagnostics disabled returns the handler's `ValueTask` directly — **no async state machine**.
- **`PublishAsync`** has a matching switch over the notification's **runtime type**; `Publish_<SafeType>` methods return `ValueTask`. The `default:` arm returns `default` (no-op) when no handler is registered. Because it matches the runtime type, base/interface-typed publishes dispatch correctly (v1's `typeof(TNotification)` dictionary lookup silently no-oped for those).
- The `case null:` arm throws `ArgumentNullException` before any handler resolution.

### `AddMediatorLite()` — DI entry point

```40:47:src/MediatorLite/Configuration/ServiceCollectionExtensions.cs
    public static IServiceCollection AddMediatorLite(this IServiceCollection services)
    {
        // TryAdd keeps this order-independent with AddGeneratedHandlers(): the generated
        // registration uses plain AddScoped, and the container resolves the last IMediator
        // descriptor, so the generated mediator wins regardless of call order.
        services.TryAddScoped<IMediator, ThrowingMediator>();
        return services;
    }
```

`AddMediatorLite()` is now an **optional diagnostic fallback**:
- It registers `ThrowingMediator` via `TryAddScoped<IMediator, ...>`. The real mediator is registered by the generated `AddGeneratedHandlers()` with plain `AddScoped<IMediator, SourceGeneratedMediator>()`.
- Because the generated registration is unconditional `AddScoped` and the container resolves the **last** `IMediator` descriptor, the generated mediator always wins. The `TryAdd` only takes effect when `AddGeneratedHandlers()` never ran — turning a missing generator into a clear `InvalidOperationException` instead of a resolution failure.
- **Call order of the two methods no longer matters.** The mediator is **Scoped** (the generated mediator captures the resolving scope's `IServiceProvider`; resolved from the root provider it behaves like a singleton). It is no longer `Transient`.

`AddMediatorLite()` takes **no arguments**. There is no `MediatorOptions` — v2 removed [src/MediatorLite/Configuration/MediatorOptions.cs](src/MediatorLite/Configuration/MediatorOptions.cs) (see git status). All configuration is compile-time via attributes.

### `ThrowingMediator` — diagnostic fallback

```9:26:src/MediatorLite/Internal/ThrowingMediator.cs
internal sealed class ThrowingMediator : IMediator
{
    private const string Message =
        "No source-generated mediator is registered. Reference the MediatorLite.SourceGeneration " +
        "analyzer package from the assembly that contains your handlers and call " +
        "services.AddGeneratedHandlers() so the generated mediator replaces this fallback.";

    public ValueTask<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(Message);

    public ValueTask PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
        => throw new InvalidOperationException(Message);
}
```

This type is registered only by `AddMediatorLite()` via `TryAddScoped`. When `AddGeneratedHandlers()` runs (the normal case), the generated `SourceGeneratedMediator` is registered after it and wins resolution, so `ThrowingMediator` never dispatches. If the generator never ran, every dispatch throws the guidance message above.

### `PipelineBehaviorTypeResolver` — removed

This v1 helper (open- vs closed-behavior interface resolution for runtime registration) was deleted: the generated mediator unrolls behaviors itself at compile time, so nothing on the v2 dispatch or registration path needs runtime behavior-type resolution. Do not reintroduce it — behavior discovery/expansion belongs to the source generator (`ExpandBehaviors` in `HandlerDiscoveryGenerator.cs`).

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
- Call `services.AddGeneratedHandlers().AddMediatorLite()`. `AddGeneratedHandlers()` is provided by the generator and registers `IMediator` (the generated `SourceGeneratedMediator`) + all handlers/behaviors/validators; `AddMediatorLite()` only adds the optional `ThrowingMediator` diagnostic fallback. Call order is interchangeable.
- Let the generator do all the heavy lifting (typed-switch dispatch, pipeline composition, logging, tracing). There is no hand-written mediator to keep small.
- Let `ValidationBehavior` aggregate errors across validators — that is intentional.
- Use `MediatorActivitySource.Source` as the subscription target in OpenTelemetry setup (`builder.AddSource("MediatorLite")`).

**Don't:**
- Don't add reflection-based dispatch. v2 dispatch is a compile-time typed switch in the generated mediator.
- Don't register `IMediator` manually — `AddGeneratedHandlers()` registers it.
- Don't expect `ThrowingMediator` to dispatch when the generator ran — the generated `SourceGeneratedMediator` always wins resolution (last `AddScoped` descriptor).
- Don't return an error from `PublishAsync` when there are no handlers. The generated switch's `default:` arm returns `default` (a no-op) — a behavioral contract verified in tests.
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

5. **Verify the missing-generator diagnostic**
   1. Register only `services.AddMediatorLite()` (no `AddGeneratedHandlers()`), build the provider, and resolve `IMediator` — you get the `ThrowingMediator` fallback.
   2. `SendAsync` / `PublishAsync` then throw `InvalidOperationException` with the "call services.AddGeneratedHandlers()" guidance — useful to verify error messaging.
   3. For positive tests, always let the test project's source generator run (it is already wired — see [tests/MediatorLite.Tests/MediatorLite.Tests.csproj](tests/MediatorLite.Tests/MediatorLite.Tests.csproj)). Resolved `IMediator` will be `MediatorLite.Generated.SourceGeneratedMediator`.

## Pitfalls & gotchas

- **Runtime-type dispatch**: the generated switch matches the request/notification's **runtime type**, which matters if callers declare the parameter as `IRequest<T>` / `INotification`. Notification dispatch by runtime type is why base/interface-typed publishes now dispatch (they silently no-oped in v1).
- **Null requests on `PublishAsync`** throw `ArgumentNullException` from the switch's `case null:` arm. Tests in [MediatorTests.cs](tests/MediatorLite.Tests/SourceGeneration/MediatorTests.cs) assert this precisely.
- **`PipelineBehaviorTypeResolver` was deleted** — do not reintroduce runtime behavior-type resolution; the generated mediator unrolls behaviors itself at compile time.
- **`MediatorOptions.cs` is deleted** (see git status `D src/MediatorLite/Configuration/MediatorOptions.cs`). Do not re-introduce it.
- **`ValidationBehavior.Validators` is stored once** in the constructor (`validators.ToList()`). Scoped handler/validator lifetimes still work because the generated mediator is Scoped and resolves them from the resolving scope's `IServiceProvider`.
- **`ValidationException` is thrown inside `await validator.ValidateAsync(...)`**; it does **not** wrap individual validator errors — all errors flatten into one exception.
- **`DiagnosticListener Listener = new("MediatorLite")`** is publicly visible — do not rename its source without coordinating with downstream diagnostic observers.

## Related skills & rules

- **mediatorlite-abstractions** — defines `IMediator` (ValueTask), `IRequest`, `IPipelineBehavior`, and the `IValidator<T>` / validation model types consumed here.
- **mediatorlite-source-generation** — emits the `SourceGeneratedMediator` (implementing `IMediator`) that owns all dispatch, plus `AddGeneratedHandlers` that registers it (`AddScoped<IMediator, SourceGeneratedMediator>`) and everything else; `AddMediatorLite` adds only the `ThrowingMediator` fallback.
- **mediatorlite-tests** — `tests/MediatorLite.Tests/SourceGeneration/MediatorTests.cs`, `ValidationTests.cs`, `PipelineBehaviorTests.cs`, and `NotificationTests.cs` exercise the generated mediator, `ValidationBehavior`, and the DI wiring.
- [AGENTS.md](AGENTS.md) — the generated `SourceGeneratedMediator` (Scoped) is the `IMediator`; v2 has no reflection fallback.
- Docs: [docs/observability.md](docs/observability.md), [docs/validation.md](docs/validation.md), [docs/pipeline-behaviors.md](docs/pipeline-behaviors.md).
