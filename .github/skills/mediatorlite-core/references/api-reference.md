# API Reference

Complete API reference for MediatorLite core library types.

---

## Interfaces

### IMediator

```csharp
namespace MediatorLite;

public interface IMediator
{
    Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    Task PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
```

- Returns `Task` (not `ValueTask`) for consumer ergonomics — enables `Task.WhenAll`, natural parallel patterns.
- `SendAsync` throws `InvalidOperationException` when no handler is registered.
- `PublishAsync` silently returns if no handlers are registered (zero handlers is valid).

### IRequest\<TResponse\> / IRequest

```csharp
namespace MediatorLite;

public interface IRequest<out TResponse>;
public interface IRequest : IRequest<Unit>;
```

- `IRequest<out TResponse>` — covariant marker for requests returning `TResponse`.
- `IRequest` — convenience for void commands, equivalent to `IRequest<Unit>`.

### IRequestHandler\<TRequest, TResponse\> / IRequestHandler\<TRequest\>

```csharp
namespace MediatorLite;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest<Unit>
{
    new ValueTask HandleAsync(TRequest request, CancellationToken cancellationToken = default);

    // Explicit interface implementation bridges void → Unit:
    async ValueTask<Unit> IRequestHandler<TRequest, Unit>.HandleAsync(TRequest request, CancellationToken ct)
    {
        await HandleAsync(request, ct);
        return Unit.Value;
    }
}
```

- Handlers return `ValueTask<TResponse>` (avoids allocation on synchronous completion paths).
- `IRequestHandler<TRequest>` convenience interface auto-wraps void handler to return `Unit.Value`.

### INotification

```csharp
namespace MediatorLite;

public interface INotification;
```

Marker interface. Zero or more handlers can be registered per notification type.

### INotificationHandler\<TNotification\>

```csharp
namespace MediatorLite;

public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    ValueTask HandleAsync(TNotification notification, CancellationToken cancellationToken = default);
}
```

### IPipelineBehavior\<TRequest, TResponse\>

```csharp
namespace MediatorLite;

public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default);
}
```

- `RequestHandlerDelegate<TResponse>` — parameterless delegate representing the next step in the pipeline.
- Behaviors can short-circuit by not calling `next()`.

### ISourceGeneratedMediator

9 methods for zero-reflection dispatch. All `Try*` methods return nullable types — `null` means "not known at compile time, fall back to reflection".

```csharp
namespace MediatorLite;

public interface ISourceGeneratedMediator
{
    // Request dispatch — null means fall back to reflection
    ValueTask<TResponse>? TrySendAsync<TResponse>(
        IServiceProvider serviceProvider,
        IRequest<TResponse> request,
        CancellationToken cancellationToken);

    // Handler invocation for behavior pipeline — null means fall back
    ValueTask<TResponse>? TryInvokeHandlerAsync<TResponse>(
        IServiceProvider serviceProvider,
        IRequest<TResponse> request,
        CancellationToken cancellationToken);

    // Handler order from compile-time attributes — null means check at runtime
    int? TryGetHandlerOrder(Type handlerType);

    // Per-notification execution/error strategy — null means use global defaults
    (NotificationExecutionStrategy ExecutionStrategy, NotificationErrorStrategy ErrorStrategy)?
        TryGetNotificationOptions(Type notificationType);

    // Typed handler resolution — null means fall back to GetServices<>()
    IReadOnlyList<INotificationHandler<TNotification>>? TryGetCachedHandlers<TNotification>(
        IServiceProvider serviceProvider)
        where TNotification : INotification;

    // Behavior resolution without MakeGenericType — null means fall back
    List<object>? TryResolveBehaviors(
        IServiceProvider serviceProvider,
        Type requestType,
        Type responseType);

    // Direct typed handler invocation (throws InvalidOperationException if type unknown)
    ValueTask<TResponse> InvokeHandler<TResponse>(
        Type requestType,
        object handler,
        object request,
        CancellationToken cancellationToken);

    // Direct typed behavior invocation (throws InvalidOperationException if type unknown)
    ValueTask<TResponse> InvokeBehavior<TResponse>(
        Type requestType,
        Type behaviorType,
        object behavior,
        object request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
```

**Nullable return semantics:**
- `Try*` methods: `null` = type not discovered at compile time → caller must fall back to reflection.
- `InvokeHandler` / `InvokeBehavior`: non-nullable return — throw `InvalidOperationException` if the type is unknown. Callers catch this and fall through to reflection.

### IValidator\<TRequest\>

```csharp
namespace MediatorLite.Validation;

public interface IValidator<in TRequest>
{
    ValueTask<ValidationResult> ValidateAsync(TRequest request, CancellationToken cancellationToken = default);
}
```

---

## Attributes

### NotificationHandlerOrderAttribute

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotificationHandlerOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}
```

- **Target:** Notification handler classes.
- **Default order:** 0 (when attribute is absent).
- **Lower values execute first.**

### NotificationOptionsAttribute

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotificationOptionsAttribute : Attribute
{
    public NotificationExecutionStrategy ExecutionStrategy { get; set; } = NotificationExecutionStrategy.Sequential;
    public NotificationErrorStrategy ErrorStrategy { get; set; } = NotificationErrorStrategy.ContinueAndAggregate;
    public bool OverrideGlobal { get; set; } = true;
}
```

- **Target:** Notification classes (not handlers).
- **OverrideGlobal:** When `true` (default), overrides `MediatorOptions` settings. When `false`, the attribute is found but ignored (global settings used).

### BehaviorOrderAttribute

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BehaviorOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}
```

- **Target:** Pipeline behavior classes.
- Currently the mediator relies on DI registration order rather than this attribute for behavior ordering, but the attribute exists for future use and source generator support.

### MediatorLoggingAttribute

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MediatorLoggingAttribute : Attribute
{
    public bool Enabled { get; set; } = true;
    public bool IncludePayload { get; set; }     // default false
    public int LogLevel { get; set; } = 2;       // LogLevel.Information
}
```

- **Target:** Request classes.
- Overrides global logging settings for specific request types.
- `LogLevel` uses `Microsoft.Extensions.Logging.LogLevel` integer values (0=Trace, 1=Debug, 2=Information, etc.).

### MediatorGenerationAttribute

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MediatorGenerationAttribute : Attribute
{
    public bool Skip { get; set; }   // default false
}
```

- **Target:** Handler/behavior classes.
- When `Skip = true`, the source generator will not auto-register this type.

---

## Enums

### NotificationExecutionStrategy

```csharp
public enum NotificationExecutionStrategy
{
    Sequential = 0,    // One handler at a time, in order
    Parallel = 1,      // All handlers concurrently via Task.WhenAll
    StopOnFirst = 2    // Stop after first handler succeeds
}
```

### NotificationErrorStrategy

```csharp
public enum NotificationErrorStrategy
{
    StopOnFirstError = 0,       // Propagate first exception immediately
    ContinueAndAggregate = 1    // Collect all exceptions, throw AggregateException
}
```

---

## MediatorOptions

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `NotificationExecutionStrategy` | `NotificationExecutionStrategy` | `Sequential` (0) | Default execution strategy for notifications |
| `NotificationErrorStrategy` | `NotificationErrorStrategy` | `ContinueAndAggregate` (1) | Default error handling for notifications |
| `EnableBuiltInLogging` | `bool` | `true` | Enables `ILogger<Mediator>` logging |
| `DefaultLogLevel` | `LogLevel` | `LogLevel.Debug` | Log level for built-in logging |
| `EnableTracing` | `bool` | `true` | Enables OpenTelemetry `ActivitySource` tracing |
| `HandlerLifetime` | `ServiceLifetime` | `Transient` | DI lifetime for handlers registered via options |
| `MediatorLifetime` | `ServiceLifetime` | `Transient` | DI lifetime for the `IMediator` registration |

### Internal Property

- `BehaviorTypes` — `internal IReadOnlyList<Type>` backed by `private readonly List<Type> _behaviorTypes`.

### AddOpenBehavior(Type behaviorType)

Validation rules (all throw `ArgumentException` on failure):

1. `ArgumentNullException.ThrowIfNull(behaviorType)` — must not be null.
2. `behaviorType.IsGenericTypeDefinition` must be `true` — must be an open generic (e.g., `typeof(LoggingBehavior<,>)`).
3. `behaviorType.GetGenericArguments().Length == 2` — must have exactly 2 type parameters.
4. Must implement `IPipelineBehavior<,>` — checked via `GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))`.

Returns `this` for method chaining.

### AddBehavior\<TBehavior\>()

```csharp
public MediatorOptions AddBehavior<TBehavior>() where TBehavior : class
```

Adds a closed behavior type to `_behaviorTypes`. No validation beyond the `class` constraint.
Returns `this` for method chaining.

---

## Unit Type

```csharp
namespace MediatorLite;

public readonly record struct Unit : IEquatable<Unit>, IComparable<Unit>
{
    public static readonly Unit Value = default;
    public static ValueTask<Unit> CompletedTask { get; } = ValueTask.FromResult(Value);
    public int CompareTo(Unit other) => 0;
    public override string ToString() => "()";
}
```

- `readonly record struct` — zero-size, no heap allocation.
- `Value` — singleton instance (all instances are equal).
- `CompletedTask` — pre-allocated `ValueTask<Unit>` for synchronous completion.
- Implements `IEquatable<Unit>` (via record) and `IComparable<Unit>` (always returns 0).
- `ToString()` returns `"()"`.
