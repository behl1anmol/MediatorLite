---
name: mediatorlite-core
description: >
  Deep knowledge of the MediatorLite core library — its abstractions, interfaces, mediator dispatch logic,
  DI registration, pipeline behaviors, notification strategies, validation, observability, and configuration.
  Use this skill whenever working on files under src/MediatorLite/, implementing or modifying IMediator,
  IRequest, IRequestHandler, INotification, INotificationHandler, IPipelineBehavior, ISourceGeneratedMediator,
  MediatorOptions, ServiceCollectionExtensions, Unit, validation types, diagnostics, or any core mediator
  dispatch logic. Also trigger when the user asks about MediatorLite architecture, how requests are routed,
  how notifications execute, how pipeline behaviors wrap handlers, how the source-gen vs reflection fallback
  works, what MediatorOptions properties exist, or how to register handlers/behaviors in DI. Even if the user
  just mentions "mediator internals", "mediator dispatch", "handler resolution", or "pipeline behavior order",
  use this skill.
---

# MediatorLite Core Library

## Architecture Overview

MediatorLite is a lightweight mediator/CQRS library for .NET. The core library lives in `src/MediatorLite/`.

### File Organization

| Folder | Purpose |
|--------|---------|
| `Abstractions/` | Public interfaces (`IMediator`, `IRequest`, `IRequestHandler`, `INotification`, `INotificationHandler`, `IPipelineBehavior`, `ISourceGeneratedMediator`, `Unit`) and attributes/enums |
| `Configuration/` | `MediatorOptions` and `ServiceCollectionExtensions` (DI registration) |
| `Diagnostics/` | `MediatorActivitySource` (OpenTelemetry) and `MediatorDiagnostics` (DiagnosticListener) |
| `Internal/` | `Mediator` — the sealed internal dispatcher implementation |
| `Validation/` | `IValidator<T>`, `DataAnnotationsValidator<T>`, `ValidationBehavior<TReq,TRes>`, `ValidationException`, models (`ValidationResult`, `ValidationError`) |

### Dual-Dispatch Model

The `Mediator` class (in `Internal/Mediator.cs`) resolves `ISourceGeneratedMediator?` from DI at construction. On every request:

1. **Source-gen path** — calls `TrySendAsync` / `TryInvokeHandlerAsync` on the source-generated implementation. Returns a `ValueTask<TResponse>?`; non-null means the request was handled.
2. **Reflection fallback** — if `ISourceGeneratedMediator` is absent or returns `null`, uses `MakeGenericType` + `ConcurrentDictionary` caching to resolve `IRequestHandler<TReq,TRes>` from DI and calls `HandleAsync` via reflection. `ExceptionDispatchInfo.Capture` preserves stack traces from `TargetInvocationException`.
3. **No handler** → `InvalidOperationException`.

### Pipeline Model

When pipeline behaviors exist:
- Behaviors are resolved via `TryResolveBehaviors` (source-gen) or `ResolveBehaviorsFromDI` (reflection).
- A `RequestHandlerDelegate<TResponse>` chain is built by wrapping from **last behavior to first** (reverse iteration), so the first-registered behavior is the **outermost** wrapper.
- A behavior can **short-circuit** by not calling `next()`.
- Behavior invocation order: source-gen `InvokeBehavior` → reflection fallback with `ExceptionDispatchInfo`.

### Notification Execution

Handlers are ordered via `NotificationHandlerOrderAttribute` (with skip-sort optimization when all orders are 0). Three execution strategies:

| Strategy | Behavior |
|----------|----------|
| `Sequential` | Handlers run one-by-one in order |
| `Parallel` | `ArrayPool<Task>.Shared.Rent`, `Task.WhenAll` |
| `StopOnFirst` | Stops after the first handler completes without error |

Error strategies: `StopOnFirstError` (re-throw immediately) or `ContinueAndAggregate` (collect all exceptions, throw `AggregateException`). `OperationCanceledException` is always re-thrown immediately regardless of error strategy.

---

## Public Interfaces

### IMediator

```csharp
public interface IMediator
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
```

Public API returns `Task` (consumer ergonomics — enables `Task.WhenAll`). Handlers internally use `ValueTask`.

### IRequest / IRequest\<TResponse\>

```csharp
public interface IRequest<out TResponse>;     // Requests with a return type
public interface IRequest : IRequest<Unit>;   // Commands with no return (uses Unit)
```

### IRequestHandler

```csharp
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit> where TRequest : IRequest<Unit>
{
    new ValueTask HandleAsync(TRequest request, CancellationToken cancellationToken = default);
    // Explicit interface wraps void HandleAsync → returns Unit.Value
}
```

### INotification / INotificationHandler

```csharp
public interface INotification;

public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    ValueTask HandleAsync(TNotification notification, CancellationToken cancellationToken = default);
}
```

### IPipelineBehavior

```csharp
public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default);
}
```

### ISourceGeneratedMediator

9 methods — all `Try*` methods return nullable types (null = fall back to reflection):

| Method | Returns |
|--------|---------|
| `TrySendAsync<TResponse>` | `ValueTask<TResponse>?` |
| `TryInvokeHandlerAsync<TResponse>` | `ValueTask<TResponse>?` |
| `TryGetHandlerOrder(Type)` | `int?` |
| `TryGetNotificationOptions(Type)` | `(NotificationExecutionStrategy, NotificationErrorStrategy)?` |
| `TryGetCachedHandlers<T>(IServiceProvider)` | `IReadOnlyList<INotificationHandler<T>>?` |
| `TryResolveBehaviors(IServiceProvider, Type, Type)` | `List<object>?` |
| `InvokeHandler<TResponse>(Type, object, object, CancellationToken)` | `ValueTask<TResponse>` |
| `InvokeBehavior<TResponse>(Type, Type, object, object, RequestHandlerDelegate<TResponse>, CancellationToken)` | `ValueTask<TResponse>` |

The last two (`InvokeHandler` / `InvokeBehavior`) throw `InvalidOperationException` if the type is unknown at compile-time — callers catch and fall back.

### IValidator\<T\>

```csharp
public interface IValidator<in TRequest>
{
    ValueTask<ValidationResult> ValidateAsync(TRequest request, CancellationToken cancellationToken = default);
}
```

---

## Attributes & Enums

| Attribute | Target | Key Properties |
|-----------|--------|---------------|
| `NotificationHandlerOrderAttribute(int order)` | Class | `Order` (lower = first, default 0) |
| `NotificationOptionsAttribute` | Class | `ExecutionStrategy`, `ErrorStrategy`, `OverrideGlobal` (default true) |
| `BehaviorOrderAttribute(int order)` | Class | `Order` (lower = first) |
| `MediatorLoggingAttribute` | Class | `Enabled` (true), `IncludePayload` (false), `LogLevel` (2 = Information) |
| `MediatorGenerationAttribute` | Class | `Skip` (false) — omits from source-gen registration |

All attributes are `sealed`, `AllowMultiple = false`, `Inherited = false`.

| Enum | Values |
|------|--------|
| `NotificationExecutionStrategy` | `Sequential = 0`, `Parallel = 1`, `StopOnFirst = 2` |
| `NotificationErrorStrategy` | `StopOnFirstError = 0`, `ContinueAndAggregate = 1` |

---

## Configuration — MediatorOptions

| Property | Type | Default |
|----------|------|---------|
| `NotificationExecutionStrategy` | `NotificationExecutionStrategy` | `Sequential` |
| `NotificationErrorStrategy` | `NotificationErrorStrategy` | `ContinueAndAggregate` |
| `EnableBuiltInLogging` | `bool` | `true` |
| `DefaultLogLevel` | `LogLevel` | `Debug` |
| `EnableTracing` | `bool` | `true` |
| `HandlerLifetime` | `ServiceLifetime` | `Transient` |
| `MediatorLifetime` | `ServiceLifetime` | `Transient` |

Methods:
- `AddOpenBehavior(Type)` — validates: not null, open generic, exactly 2 type params, implements `IPipelineBehavior<,>`. Returns `this` for chaining.
- `AddBehavior<TBehavior>()` — adds a closed behavior type. Returns `this`.

`BehaviorTypes` is exposed as `internal IReadOnlyList<Type>`.

---

## DI Registration

### AddMediatorLite()

In `ServiceCollectionExtensions`:
1. Creates `MediatorOptions`, invokes optional `configure` action.
2. Registers `MediatorOptions` as singleton.
3. Registers `IMediator → Mediator` with `options.MediatorLifetime`.
4. Iterates `options.BehaviorTypes` — open generics registered as `IPipelineBehavior<,>`, closed types registered against their specific `IPipelineBehavior<TReq,TRes>` interface.

### AddMediatorBehavior\<T\>()

Registers a behavior directly in DI (open or closed generic), with configurable `ServiceLifetime` (default `Transient`).

### Expected companion call

`AddGeneratedHandlers()` (source-generated) should be called **before** `AddMediatorLite()` to register all handlers, notification handlers, behaviors, and `ISourceGeneratedMediator`.

---

## Error Handling

| Scenario | Exception |
|----------|-----------|
| No handler for request | `InvalidOperationException` |
| Validation failure | `ValidationException` (exposes `IReadOnlyList<ValidationError>`) |
| Notification handlers fail with `ContinueAndAggregate` | `AggregateException` |
| `OperationCanceledException` in notification handlers | Always re-thrown immediately |
| Reflection `TargetInvocationException` | Unwrapped via `ExceptionDispatchInfo.Capture(ex.InnerException).Throw()` |

---

## Observability

### OpenTelemetry Tracing

`MediatorActivitySource.Source` — name `"MediatorLite"`, version `"1.0.0"`.

Activity names: `MediatorLite.Send`, `MediatorLite.Publish`, `MediatorLite.Behavior`, `MediatorLite.NotificationHandler`.

Tags: `mediatorlite.request.type`, `mediatorlite.response.type`, `mediatorlite.notification.type`, `mediatorlite.handler.type`, `mediatorlite.behavior.type`, `mediatorlite.handler.count`, `mediatorlite.execution.strategy`, `error`, `error.message`.

### Built-in Logging

Uses `ILogger<Mediator>` at `options.DefaultLogLevel`. Logs request send/complete, notification publish, handler counts, errors. Controlled by `EnableBuiltInLogging`.

### DiagnosticListener

`MediatorDiagnostics.Listener` — name `"MediatorLite"`. Events: `RequestStarted`, `RequestCompleted`, `RequestFailed`, `NotificationPublished`, `NotificationHandlerStarted`, `NotificationHandlerCompleted`.

---

## Critical Rules

1. **No reflection** — never introduce new reflection paths; rely on `ISourceGeneratedMediator` for zero-reflection dispatch.
2. **ValueTask for handlers, Task for public API** — handlers return `ValueTask<T>` (avoids allocation on sync paths); `IMediator` returns `Task<T>` (consumer ergonomics: `Task.WhenAll`, etc.).
3. **Sealed classes** — all internal/concrete classes are `sealed` for performance (JIT devirtualization).
4. **Target framework** — `net10.0`, C# `latest`, nullable enabled, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`.
5. **Package dependencies** — only `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Logging.Abstractions` (both 9.0.0).

---

## References

For detailed deep-dives, read the relevant file from `references/`:

- `references/architecture.md` — complete file inventory, namespace map, source-gen vs reflection dispatch flow, pipeline execution model, notification execution details
- `references/api-reference.md` — full interface signatures, all 9 ISourceGeneratedMediator methods, attribute details, enum values, MediatorOptions validation rules, Unit type
- `references/validation.md` — IValidator, DataAnnotationsValidator, ValidationBehavior pipeline, ValidationResult/ValidationError models, ValidationException, registration pattern
- `references/conventions.md` — namespace conventions, ValueTask vs Task rationale, sealed class policy, no-reflection principle, error handling patterns, build configuration
