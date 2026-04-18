---
name: mediatorlite-abstractions
description: Reference for the MediatorLite.Abstractions project -- IMediator, IRequest, IRequestHandler, INotification, INotificationHandler, IPipelineBehavior, ISourceGeneratedMediator, Unit, IValidator, validation models, and every attribute in Attributes.cs (NotificationHandlerOrder, NotificationExecution, NotificationError, DefaultNotificationExecution, DefaultNotificationError, BehaviorOrder, MediatorGeneration [obsolete], DisableMediatorLogging, DisableMediatorTracing). Use when editing contracts, adding new abstractions, or understanding the public surface consumers implement.
triggers: IMediator, IRequest, IRequestHandler, INotification, INotificationHandler, IPipelineBehavior, ISourceGeneratedMediator, Unit, mediator abstractions, notification attributes, behavior order, NotificationExecutionAttribute, NotificationErrorAttribute, DisableMediatorLogging, DisableMediatorTracing, ValidationException, IValidator, RequestDispatcher, NotificationPublisher, boxing tradeoff
---

# MediatorLite.Abstractions

## Purpose

`MediatorLite.Abstractions` is the public contract layer of MediatorLite. It contains every interface, attribute, delegate, and validation model that consumers implement or that the source generator and runtime depend on. The project deliberately has **no runtime code, no DI dependencies, and no reflection** — only attributes, interfaces, and small value types. This means the abstractions can be referenced from analyzers, source generators, and consumer libraries without pulling in `Microsoft.Extensions.*`.

## When to use

- Adding or modifying a core contract (`IMediator`, `IRequest<T>`, `IRequestHandler<,>`, `INotification`, `INotificationHandler<>`, `IPipelineBehavior<,>`).
- Adding, deprecating, or tuning a compile-time attribute consumed by `HandlerDiscoveryGenerator`.
- Touching the `ISourceGeneratedMediator` contract or the `RequestDispatcher` / `NotificationPublisher` delegate signatures.
- Evolving the `Unit` struct, the validation surface (`IValidator<T>`, `ValidationResult`, `ValidationError`, `ValidationException`), or strategy enums.
- Reviewing the boxing tradeoff on the request dispatch path.

## Project location & entry points

- [MediatorLite.Abstractions.csproj](src/MediatorLite.Abstractions/MediatorLite.Abstractions.csproj)
- Core interfaces folder: [src/MediatorLite.Abstractions/Abstractions/](src/MediatorLite.Abstractions/Abstractions)
  - [IMediator.cs](src/MediatorLite.Abstractions/Abstractions/IMediator.cs)
  - [IRequest.cs](src/MediatorLite.Abstractions/Abstractions/IRequest.cs)
  - [IRequestHandler.cs](src/MediatorLite.Abstractions/Abstractions/IRequestHandler.cs)
  - [INotification.cs](src/MediatorLite.Abstractions/Abstractions/INotification.cs)
  - [INotificationHandler.cs](src/MediatorLite.Abstractions/Abstractions/INotificationHandler.cs)
  - [IPipelineBehavior.cs](src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs)
  - [ISourceGeneratedMediator.cs](src/MediatorLite.Abstractions/Abstractions/ISourceGeneratedMediator.cs)
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

The public mediator surface exposes `Task`/`Task<T>` (not `ValueTask`) for ergonomics with `Task.WhenAll`. Handlers and behaviors internally use `ValueTask`.

```42:75:src/MediatorLite.Abstractions/Abstractions/IMediator.cs
public interface IMediator
{
    /// <summary>
    /// Sends a request to a single handler and returns the response.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from the handler.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="Task{TResponse}"/> representing the response from the handler.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the request type.</exception>
    /// <remarks>
    /// Returns <see cref="Task{TResponse}"/> for consumer ergonomics, enabling parallel patterns.
    /// Handlers internally use <see cref="ValueTask{TResponse}"/> for synchronous completion optimization.
    /// </remarks>
    Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification to all registered handlers.
    /// </summary>
    Task PublishAsync<TNotification>(
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

### `ISourceGeneratedMediator` — compile-time dispatch contract

The runtime `Mediator` delegates to this interface. The source generator emits a `SourceGeneratedMediator : ISourceGeneratedMediator` that contains pre-built `Dictionary<Type, RequestDispatcher>` and `Dictionary<Type, NotificationPublisher>` tables.

```26:46:src/MediatorLite.Abstractions/Abstractions/ISourceGeneratedMediator.cs
public delegate Task<object> RequestDispatcher(
    IServiceProvider serviceProvider,
    object request,
    CancellationToken cancellationToken);

/// <summary>
/// Delegate for publishing a notification to all handlers.
/// </summary>
public delegate Task NotificationPublisher(
    IServiceProvider serviceProvider,
    object notification,
    CancellationToken cancellationToken);
```

```63:96:src/MediatorLite.Abstractions/Abstractions/ISourceGeneratedMediator.cs
public interface ISourceGeneratedMediator
{
    /// <summary>
    /// Gets the dispatch delegate for a request type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    RequestDispatcher? GetDispatcher(Type requestType);

    /// <summary>
    /// Gets the publish delegate for a notification type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    NotificationPublisher? GetPublisher(Type notificationType);
}
```

#### Boxing tradeoff on `RequestDispatcher`

`RequestDispatcher` returns `Task<object>` rather than a generic `Task<TResponse>`. This causes **heap boxing for value-type responses** (`int`, `bool`, `Guid`, custom structs, `Unit`). The design is documented in-source:

```12:29:src/MediatorLite.Abstractions/Abstractions/ISourceGeneratedMediator.cs
/// <remarks>
/// <para>
/// <b>Boxing tradeoff:</b> This delegate returns <c>Task&lt;object&gt;</c> which causes boxing
/// for value type responses (e.g., <c>int</c>, <c>bool</c>, <c>Guid</c>, custom structs).
/// Each value type response incurs a heap allocation when boxed to <c>object</c>.
/// </para>
/// <para>
/// This is a deliberate design tradeoff for compile-time simplicity: a single delegate signature
/// allows the source generator to produce a unified dispatch table (<c>Dictionary&lt;Type, RequestDispatcher&gt;</c>)
/// without requiring generic delegate instantiation per request type. The boxing cost is typically
/// negligible compared to I/O-bound handler work, but may be measurable in high-throughput,
/// CPU-bound scenarios with value type responses.
/// </para>
/// </remarks>
```

`NotificationPublisher` returns `Task` (non-generic), so publishing does **not** allocate for the return path.

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

When either attribute is applied at assembly level, the generator omits the corresponding `ILogger<IMediator>.LogDebug(...)` calls or `ActivitySource.StartActivity(...)` calls from every emitted `Pipeline_*` / `Publish_*` method. These are **compile-time switches**; there is no runtime toggle.

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
- Don't rely on reflection fallback — v2 removed it; the runtime `Mediator` requires a registered `ISourceGeneratedMediator`.

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

- **Boxing on value-type responses**: `Task<object>` boxes every `int` / `bool` / `Guid` / `Unit` returned from a handler. Usually negligible vs. I/O, but matters for tight CPU-bound benchmarks. See [docs/benchmarks.md](docs/benchmarks.md).
- **`IRequest : IRequest<Unit>` double-dispatch**: When you implement `IRequestHandler<TRequest>` (void overload), the explicit interface method **must not** be overridden — the auto-generated `IRequestHandler<TRequest, Unit>.HandleAsync` calls your void overload and returns `Unit.Value`.
- **`[MediatorGeneration(Skip = true)]` silently skips everything**: the generator drops the handler/behavior/validator from **all** outputs, not just DI registration. Don't use it.
- **Assembly-level opt-out attributes affect the whole compilation unit**, not just a specific file. If your library is consumed downstream, the consumer may want logging back on — consider exposing a compile-time configuration alternative.
- **`[NotificationExecution]` / `[NotificationError]` on a handler has no effect** — they target the notification class. The generator's `GetNotificationInfo` only reads them off `INotification` implementations.
- **`DefaultNotificationExecutionAttribute` must match the notification assembly**. The generator reads `compilation.Assembly.GetAttributes()`, so the default must live in the **same assembly as the notification types** (or any assembly participating in the same compilation).
- **`ArgumentOutOfRangeException` style**: `ArgumentNullException.ThrowIfNull(request)` on `SendAsync` triggers before the dispatcher is looked up; `null` notifications throw `ArgumentNullException` too (see runtime `Mediator.cs`).

## Related skills & rules

- **mediatorlite-core** — how `Mediator.cs`, `ServiceCollectionExtensions`, `NullSourceGeneratedMediator`, and validation runtime consume these abstractions.
- **mediatorlite-source-generation** — how `HandlerDiscoveryGenerator` reads every attribute and interface defined here and emits code in the `MediatorLite.Generated` namespace.
- **mediatorlite-tests** — `tests/MediatorLite.Tests/UnitTests/AttributeTests.cs` and `UnitTests/ValidationTests.cs` give direct coverage of the types in this project.
- Workspace rule: [AGENTS.md](AGENTS.md) — always use `IRequest<Unit>` for commands and keep changes aligned with `ISourceGeneratedMediator`.
- Relevant docs: [docs/notifications.md](docs/notifications.md), [docs/validation.md](docs/validation.md), [docs/pipeline-behaviors.md](docs/pipeline-behaviors.md), [docs/observability.md](docs/observability.md).
