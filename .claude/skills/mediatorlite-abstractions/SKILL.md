---
name: mediatorlite-abstractions
description: Reference for the MediatorLite.Abstractions project -- IMediator (ValueTask dispatch), IRequest, IRequestHandler, INotification, INotificationHandler, IPipelineBehavior, Unit, IValidator, validation models, and every attribute in Attributes.cs (NotificationHandlerOrder, NotificationExecution, NotificationError, DefaultNotificationExecution, DefaultNotificationError, BehaviorOrder, MediatorGeneration [obsolete], DisableMediatorLogging, DisableMediatorTracing). Use when editing contracts, adding new abstractions, or understanding the public surface consumers implement.
triggers: IMediator, IRequest, IRequestHandler, INotification, INotificationHandler, IPipelineBehavior, Unit, mediator abstractions, notification attributes, behavior order, NotificationExecutionAttribute, NotificationErrorAttribute, DisableMediatorLogging, DisableMediatorTracing, ValidationException, IValidator, ValueTask dispatch, SourceGeneratedMediator, typed switch dispatch
---

# MediatorLite.Abstractions

## Purpose

`MediatorLite.Abstractions` is the public contract layer of MediatorLite. It contains every interface, attribute, delegate, and validation model that consumers implement or that the source generator and runtime depend on. The project deliberately has **no runtime code, no DI dependencies, and no reflection** — only attributes, interfaces, and small value types. This means the abstractions can be referenced from analyzers, source generators, and consumer libraries without pulling in `Microsoft.Extensions.*`.

## When to use

- Adding or modifying a core contract (`IMediator`, `IRequest<T>`, `IRequestHandler<,>`, `INotification`, `INotificationHandler<>`, `IPipelineBehavior<,>`).
- Adding, deprecating, or tuning a compile-time attribute consumed by `HandlerDiscoveryGenerator`.
- Understanding how the generated `SourceGeneratedMediator` implements `IMediator` directly via a compile-time typed switch.
- Evolving the `Unit` struct, the validation surface (`IValidator<T>`, `ValidationResult`, `ValidationError`, `ValidationException`), or strategy enums.

## Project location & entry points

- [MediatorLite.Abstractions.csproj](src/MediatorLite.Abstractions/MediatorLite.Abstractions.csproj)
- Core interfaces folder: [src/MediatorLite.Abstractions/Abstractions/](src/MediatorLite.Abstractions/Abstractions)
  - [IMediator.cs](src/MediatorLite.Abstractions/Abstractions/IMediator.cs)
  - [IRequest.cs](src/MediatorLite.Abstractions/Abstractions/IRequest.cs)
  - [IRequestHandler.cs](src/MediatorLite.Abstractions/Abstractions/IRequestHandler.cs)
  - [INotification.cs](src/MediatorLite.Abstractions/Abstractions/INotification.cs)
  - [INotificationHandler.cs](src/MediatorLite.Abstractions/Abstractions/INotificationHandler.cs)
  - [IPipelineBehavior.cs](src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs)
  - [Unit.cs](src/MediatorLite.Abstractions/Abstractions/Unit.cs)
  - [Attributes.cs](src/MediatorLite.Abstractions/Abstractions/Attributes.cs)
- Validation folder: [src/MediatorLite.Abstractions/Validation/](src/MediatorLite.Abstractions/Validation)
  - [IValidator.cs](src/MediatorLite.Abstractions/Validation/IValidator.cs)
  - [ValidationException.cs](src/MediatorLite.Abstractions/Validation/ValidationException.cs)
  - [Models/ValidationResult.cs](src/MediatorLite.Abstractions/Validation/Models/ValidationResult.cs)
  - [Models/ValidationError.cs](src/MediatorLite.Abstractions/Validation/Models/ValidationError.cs)
- Target framework: `net10.0`, nullable + implicit usings on, warnings-as-errors (see [Directory.Build.props](Directory.Build.props)). Namespace: `MediatorLite` for the core surface, `MediatorLite.Validation` / `MediatorLite.Validation.Models` for validation.

## Core types / API surface

### `IMediator` — the public mediator contract

The public mediator surface is `ValueTask`-based end-to-end (`ValueTask<TResponse>` for `SendAsync`, `ValueTask` for `PublishAsync`) so a synchronously completing handler with no behaviors allocates nothing. A `ValueTask` must be consumed exactly once — `await` it directly; for `Task.WhenAll`, fan-out, or storing the result, convert it first with `.AsTask()`. The generated `SourceGeneratedMediator` (namespace `MediatorLite.Generated`) implements this interface directly.

```44:77:src/MediatorLite.Abstractions/Abstractions/IMediator.cs
public interface IMediator
{
    /// <summary>
    /// Sends a request to a single handler and returns the response.
    /// </summary>
    /// <returns>A <see cref="ValueTask{TResponse}"/> representing the response from the handler.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the request type.</exception>
    ValueTask<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification to all registered handlers.
    /// </summary>
    ValueTask PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
```

### Requests and the `IRequest` / `IRequest<TResponse>` split

`IRequest` is a convenience interface equivalent to `IRequest<Unit>` — use it for commands with no meaningful return value.

```17:33:src/MediatorLite.Abstractions/Abstractions/IRequest.cs
public interface IRequest<out TResponse>;

/// <summary>
/// Marker interface for requests that don't return a meaningful response.
/// </summary>
public interface IRequest : IRequest<Unit>;
```

### Request handlers (`IRequestHandler<,>` + void convenience overload)

Handlers return `ValueTask<TResponse>`. The `IRequestHandler<TRequest>` overload lets a command handler return `ValueTask` directly; explicit interface implementation adapts it to `Unit`.

```31:41:src/MediatorLite.Abstractions/Abstractions/IRequestHandler.cs
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles a request asynchronously.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ValueTask{TResponse}"/> representing the asynchronous operation with the response.</returns>
    ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}
```

```62:81:src/MediatorLite.Abstractions/Abstractions/IRequestHandler.cs
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest<Unit>
{
    new ValueTask HandleAsync(TRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicit implementation that wraps the void HandleAsync to return Unit.
    /// </summary>
    async ValueTask<Unit> IRequestHandler<TRequest, Unit>.HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        await HandleAsync(request, cancellationToken);
        return Unit.Value;
    }
}
```

### Notifications

Notifications are fire-and-forget pub/sub: zero or more handlers, no return value.

```15:15:src/MediatorLite.Abstractions/Abstractions/INotification.cs
public interface INotification;
```

```23:33:src/MediatorLite.Abstractions/Abstractions/INotificationHandler.cs
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Handles a notification asynchronously.
    /// </summary>
    /// <param name="notification">The notification to handle.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask HandleAsync(TNotification notification, CancellationToken cancellationToken = default);
}
```

### Pipeline behavior + `RequestHandlerDelegate<TResponse>`

```1:8:src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs
namespace MediatorLite;

/// <summary>
/// Delegate representing the next handler or behavior in the request pipeline.
/// </summary>
/// <typeparam name="TResponse">The type of response from the handler.</typeparam>
/// <returns>A <see cref="ValueTask{TResponse}"/> representing the response.</returns>
public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>();
```

```45:59:src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles the request by optionally performing work before/after invoking the next handler.
    /// </summary>
    ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default);
}
```

A behavior **short-circuits** the pipeline by not calling `next()`. Order is controlled with `[BehaviorOrder(int)]` — lower values run first.

### Dispatch is the generated `SourceGeneratedMediator` — no separate contract

There is **no** `ISourceGeneratedMediator` interface, `RequestDispatcher` / `NotificationPublisher` delegate, or `Dictionary<Type, ...>` dispatch table in v2 — they were all deleted. The source generator emits a single `SourceGeneratedMediator : global::MediatorLite.IMediator` (namespace `MediatorLite.Generated`) that implements `IMediator` **directly**.

Dispatch is a compile-time C# **type-pattern switch** over the concrete request/notification types (arms emitted most-derived-first), each arm calling a fully typed per-request method:

```csharp
// Emitted shape (namespace MediatorLite.Generated)
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
                var vt = Send_MyQuery(r, ct);            // ValueTask<MyResult>
                if (typeof(TResponse) == typeof(MyResult))
                    return Unsafe.As<ValueTask<MyResult>, ValueTask<TResponse>>(ref vt);
                return SlowCast<MyResult, TResponse>(vt); // covariant IRequest<out T> fallback
            }
            case null: throw new ArgumentNullException(nameof(request));
            default:   throw new InvalidOperationException(/* no handler */);
        }
    }
    // ...PublishAsync switch, Send_<Type>/Publish_<Type> methods using _sp...
}
```

Key properties of the generated dispatch:

- **No boxing.** The exact-type `ValueTask<TConcrete>` result is converted to `ValueTask<TResponse>` via an identity-guarded `System.Runtime.CompilerServices.Unsafe.As` (the `typeof` guard JIT-folds to a constant, so the reinterpret is free). Value-type responses stay typed — there is no `Task<object>`. v1 boxed every value-type response through `Task<object>`; **v2 eliminated that entirely.**
- **`SlowCast` fallback.** Covariant `IRequest<out T>` dispatch (where `TResponse` is a wider reference type than the concrete response) falls back to a `SlowCast` reference cast — still no value-type boxing.
- **Per-request methods are `Send_<SafeType>`** (instance methods returning `ValueTask<TResponse>`, using the `_sp` `IServiceProvider` field). A zero-behavior request with diagnostics disabled returns the handler's `ValueTask` directly — **no async state machine**.
- **Publishers are `Publish_<SafeType>`** returning `ValueTask`. The notification switch matches the **runtime type**, so base/interface-typed publishes dispatch correctly (v1's `typeof(TNotification)` dictionary lookup silently no-oped for those).

### `Unit`

```10:35:src/MediatorLite.Abstractions/Abstractions/Unit.cs
public readonly record struct Unit : IEquatable<Unit>, IComparable<Unit>
{
    /// <summary>
    /// Gets the singleton <see cref="Unit"/> value.
    /// </summary>
    public static readonly Unit Value = default;

    /// <summary>
    /// Returns a completed <see cref="ValueTask{Unit}"/> with the default <see cref="Unit"/> value.
    /// </summary>
    public static ValueTask<Unit> CompletedTask { get; } = ValueTask.FromResult(Value);

    /// <summary>
    /// Compares this instance with another <see cref="Unit"/> instance.
    /// All <see cref="Unit"/> instances are considered equal.
    /// </summary>
    public int CompareTo(Unit other) => 0;

    /// <summary>
    /// Returns a string representation of this <see cref="Unit"/> instance.
    /// </summary>
    /// <returns>The string "()".</returns>
    public override string ToString() => "()";
}
```

### Validation surface

`IValidator<in TRequest>` is the pluggable contract; every discovered implementation is registered automatically by the source generator.

```9:18:src/MediatorLite.Abstractions/Validation/IValidator.cs
public interface IValidator<in TRequest>
{
    /// <summary>
    /// Validates the specified request.
    /// </summary>
    ValueTask<ValidationResult> ValidateAsync(TRequest request, CancellationToken cancellationToken = default);
}
```

`ValidationResult` is immutable (`private init` setters) with a cached `Success` instance and two `Failure` factories:

```6:49:src/MediatorLite.Abstractions/Validation/Models/ValidationResult.cs
public sealed class ValidationResult
{
    public static ValidationResult Success { get; } = new() { IsValid = true };
    public bool IsValid { get; private init; }
    public IReadOnlyList<ValidationError> Errors { get; private init; } = [];
    public static ValidationResult Failure(params ValidationError[] errors)
    {
        return new ValidationResult
        {
            IsValid = false,
            Errors = errors
        };
    }
    public static ValidationResult Failure(IEnumerable<ValidationError> errors)
    {
        return new ValidationResult
        {
            IsValid = false,
            Errors = errors.ToList()
        };
    }
}
```

`ValidationError` is a record with a nullable `AttemptedValue`:

```9:12:src/MediatorLite.Abstractions/Validation/Models/ValidationError.cs
public sealed record ValidationError(
    string PropertyName,
    string ErrorMessage,
    object? AttemptedValue = null);
```

`ValidationException` builds a human-readable message from the errors and exposes `Errors`:

```9:44:src/MediatorLite.Abstractions/Validation/ValidationException.cs
public sealed class ValidationException : Exception
{
    public IReadOnlyList<ValidationError> Errors { get; }

    public ValidationException(IEnumerable<ValidationError> errors)
        : this([.. errors])
    {
    }

    private ValidationException(IReadOnlyList<ValidationError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    public ValidationException(string message, IEnumerable<ValidationError> errors)
        : base(message)
    {
        Errors = errors.ToList();
    }
```

### Attributes (all in `Attributes.cs`)

#### Notification strategies (enums)

```6:48:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
public enum NotificationExecutionStrategy
{
    /// <summary>
    /// Execute handlers one after another in order.
    /// This is the default strategy.
    /// </summary>
    Sequential = 0,

    /// <summary>
    /// Execute all handlers concurrently using Task.WhenAll.
    /// </summary>
    Parallel = 1,

    /// <summary>
    /// Stop execution after the first handler completes successfully.
    /// </summary>
    ...
    StopOnFirst = 2
}
```

```53:76:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
public enum NotificationErrorStrategy
{
    /// <summary>
    /// Stop execution immediately when a handler throws an exception.
    /// </summary>
    StopOnFirstError = 0,

    /// <summary>
    /// Continue executing all handlers even if some throw exceptions.
    /// All exceptions are collected and thrown as an <see cref="AggregateException"/>.
    /// </summary>
    ...
    ContinueAndAggregate = 1
}
```

#### `NotificationHandlerOrderAttribute` — handler execution order (class target)

```84:91:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotificationHandlerOrderAttribute(int order) : Attribute
{
    /// <summary>
    /// Gets the execution order. Lower values execute first.
    /// </summary>
    public int Order { get; } = order;
}
```

#### `NotificationExecutionAttribute` / `NotificationErrorAttribute` — per-type compile-time strategy

```109:116:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotificationExecutionAttribute(NotificationExecutionStrategy strategy) : Attribute
{
    /// <summary>
    /// Gets the execution strategy for this notification type.
    /// </summary>
    public NotificationExecutionStrategy Strategy { get; } = strategy;
}
```

```134:141:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotificationErrorAttribute(NotificationErrorStrategy strategy) : Attribute
{
    /// <summary>
    /// Gets the error handling strategy for this notification type.
    /// </summary>
    public NotificationErrorStrategy Strategy { get; } = strategy;
}
```

Both attributes are **compile-time only**; the source generator inlines the resolved strategy into each generated `Publish_*` method.

#### `DefaultNotificationExecutionAttribute` / `DefaultNotificationErrorAttribute` — assembly-wide defaults

```159:166:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DefaultNotificationExecutionAttribute(NotificationExecutionStrategy strategy) : Attribute
{
    /// <summary>
    /// Gets the assembly-wide default execution strategy.
    /// </summary>
    public NotificationExecutionStrategy Strategy { get; } = strategy;
}
```

```184:191:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DefaultNotificationErrorAttribute(NotificationErrorStrategy strategy) : Attribute
{
    /// <summary>
    /// Gets the assembly-wide default error handling strategy.
    /// </summary>
    public NotificationErrorStrategy Strategy { get; } = strategy;
}
```

**Resolution order** (see [HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs) `ResolveStrategies`): per-notification attribute → assembly default → library default (`Sequential` for execution, `StopOnFirstError` for error).

#### `BehaviorOrderAttribute` — pipeline order

```200:207:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BehaviorOrderAttribute(int order) : Attribute
{
    /// <summary>
    /// Gets the execution order. Lower values execute first.
    /// </summary>
    public int Order { get; } = order;
}
```

Validation behaviors are emitted **before** any other ordered behavior for validated request types (see `HandlerDiscoveryGenerator.Execute`).

#### Obsolete: `MediatorGenerationAttribute`

```216:225:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[Obsolete("This attribute is no longer valid with the complete source generator implementation.")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MediatorGenerationAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether to skip source generation for this type.
    /// Default is false.
    /// </summary>
    public bool Skip { get; set; }
}
```

The generator still honors `[MediatorGeneration(Skip = true)]` when present (see `GetHandlerInfo`, `GetBehaviorInfo`, `GetValidatorInfo` in the generator), but **do not use it in new code**.

#### Observability opt-outs (assembly-level, no-arg)

```246:247:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DisableMediatorLoggingAttribute : Attribute { }
```

```267:268:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DisableMediatorTracingAttribute : Attribute { }
```

When either attribute is applied at assembly level, the generator omits the corresponding `ILogger<IMediator>.LogDebug(...)` calls or `ActivitySource.StartActivity(...)` calls from every emitted `Send_*` / `Publish_*` method. These are **compile-time switches**; there is no runtime toggle.

## Patterns & invariants

**Do:**
- Implement `IRequest<T>` / `IRequest` on records or classes; use records for value-semantics equality in CQRS.
- Use `ValueTask` in handlers and behaviors. Return `ValueTask.CompletedTask` or `Unit.CompletedTask` for synchronous paths.
- Mark handlers `public` and concrete (no `abstract`) — the generator skips abstract classes (see `GetHandlerInfo`).
- Put `[NotificationHandlerOrder]` on the handler class, not the notification class.
- Put `[NotificationExecution]` / `[NotificationError]` on the notification class, not the handler.
- Apply `[assembly: DisableMediatorLogging]` / `[assembly: DisableMediatorTracing]` in benchmark / perf-sensitive projects (see [tests/MediatorLite.Benchmarks/AssemblyInfo.cs](tests/MediatorLite.Benchmarks/AssemblyInfo.cs) and [tests/MediatorLite.RestApiBenchmarks/AssemblyInfo.cs](tests/MediatorLite.RestApiBenchmarks/AssemblyInfo.cs)).

**Don't:**
- Don't keep the `MediatorGenerationAttribute` — it is obsolete.
- Don't change `IMediator`'s public signature without also updating the generator emission in [HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs).
- Don't add runtime logic to `MediatorLite.Abstractions`. It must stay dependency-free; behaviors and validation runtime live in [src/MediatorLite/](src/MediatorLite).
- Don't expect a reflection fallback — there has never been one in v2. Without `AddGeneratedHandlers()` the only `IMediator` in the container is the `ThrowingMediator` diagnostic fallback, which throws on every dispatch.

## Common tasks

1. **Add a new request contract**
   1. Create `public sealed record MyQuery(...) : IRequest<MyResponse>;` in the consumer project.
   2. Create a handler implementing `IRequestHandler<MyQuery, MyResponse>` with a `ValueTask<MyResponse> HandleAsync(...)` method.
   3. No attribute needed — the generator discovers it via `AllInterfaces` matching `MediatorLite.IRequestHandler<TRequest, TResponse>`.

2. **Add a compile-time notification strategy**
   1. Put `[NotificationExecution(NotificationExecutionStrategy.Parallel)]` and optionally `[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]` on the `INotification` record.
   2. The generator inlines the chosen strategy into the emitted `Publish_*` method — no registration call needed.

3. **Set an assembly-wide notification default**
   1. In any source file (typically `AssemblyInfo.cs`), add `[assembly: DefaultNotificationExecution(NotificationExecutionStrategy.Sequential)]` and/or `[assembly: DefaultNotificationError(NotificationErrorStrategy.ContinueAndAggregate)]`.

4. **Add a new attribute that the generator should read**
   1. Define it in [Attributes.cs](src/MediatorLite.Abstractions/Abstractions/Attributes.cs).
   2. Teach the generator to read it via `attr.AttributeClass?.Name == "MyAttribute"` inside `GetHandlerInfo` / `GetNotificationInfo` / `GetBehaviorInfo` / `GetValidatorInfo` / `GetAssemblyDefaults`.
   3. Add a unit test to `MediatorLite.Tests/UnitTests/AttributeTests.cs`.

5. **Add a custom validator**
   1. Implement `IValidator<MyCommand>` in the consumer project (non-generic — the generator filters open generics out of validator discovery).
   2. The generator registers it via `AddGeneratedValidators` and inserts `ValidationBehavior<MyCommand, TResponse>` first in the emitted pipeline.

## Pitfalls & gotchas

- **No response boxing**: responses stay typed (`ValueTask<TResponse>`) end-to-end through the generated typed switch; there is no `Task<object>` and no value-type boxing. (v1 boxed; v2 does not.) See [docs/benchmarks.md](docs/benchmarks.md).
- **`IRequest : IRequest<Unit>` double-dispatch**: When you implement `IRequestHandler<TRequest>` (void overload), the explicit interface method **must not** be overridden — the auto-generated `IRequestHandler<TRequest, Unit>.HandleAsync` calls your void overload and returns `Unit.Value`.
- **`[MediatorGeneration(Skip = true)]` silently skips everything**: the generator drops the handler/behavior/validator from **all** outputs, not just DI registration. Don't use it.
- **Assembly-level opt-out attributes affect the whole compilation unit**, not just a specific file. If your library is consumed downstream, the consumer may want logging back on — consider exposing a compile-time configuration alternative.
- **`[NotificationExecution]` / `[NotificationError]` on a handler has no effect** — they target the notification class. The generator's `GetNotificationInfo` only reads them off `INotification` implementations.
- **`DefaultNotificationExecutionAttribute` must match the notification assembly**. The generator reads `compilation.Assembly.GetAttributes()`, so the default must live in the **same assembly as the notification types** (or any assembly participating in the same compilation).
- **Null guarding**: the generated `SendAsync` / `PublishAsync` switches have a `case null:` arm that throws `ArgumentNullException` before any handler resolution (see the generated `SourceGeneratedMediator.g.cs`).

## Related skills & rules

- **mediatorlite-core** — how `ServiceCollectionExtensions`, the `ThrowingMediator` diagnostic fallback, and validation runtime consume these abstractions (the `IMediator` implementation itself is generated, not hand-written).
- **mediatorlite-source-generation** — how `HandlerDiscoveryGenerator` reads every attribute and interface defined here and emits the `SourceGeneratedMediator` (implementing `IMediator`) in the `MediatorLite.Generated` namespace.
- **mediatorlite-tests** — `tests/MediatorLite.Tests/UnitTests/AttributeTests.cs` and `UnitTests/ValidationTests.cs` give direct coverage of the types in this project.
- Workspace rule: [AGENTS.md](AGENTS.md) — always use `IRequest<Unit>` for commands and keep changes aligned with the generated `SourceGeneratedMediator`.
- Relevant docs: [docs/notifications.md](docs/notifications.md), [docs/validation.md](docs/validation.md), [docs/pipeline-behaviors.md](docs/pipeline-behaviors.md), [docs/observability.md](docs/observability.md).
