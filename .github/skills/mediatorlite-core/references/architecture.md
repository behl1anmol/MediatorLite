# Architecture Reference

Detailed architecture documentation for the MediatorLite core library (`src/MediatorLite/`).

---

## Complete File Inventory

| Path | Purpose |
|------|---------|
| `src/MediatorLite/MediatorLite.csproj` | Project file — targets net10.0, references DI and Logging abstractions (9.0.0) |
| `src/MediatorLite/Abstractions/IMediator.cs` | Public mediator interface: `SendAsync<TResponse>`, `PublishAsync<TNotification>` |
| `src/MediatorLite/Abstractions/IRequest.cs` | Marker interfaces: `IRequest<out TResponse>` and `IRequest` (= `IRequest<Unit>`) |
| `src/MediatorLite/Abstractions/IRequestHandler.cs` | Handler interfaces: `IRequestHandler<TRequest, TResponse>` and convenience `IRequestHandler<TRequest>` |
| `src/MediatorLite/Abstractions/INotification.cs` | Marker interface: `INotification` |
| `src/MediatorLite/Abstractions/INotificationHandler.cs` | Handler interface: `INotificationHandler<TNotification>` |
| `src/MediatorLite/Abstractions/IPipelineBehavior.cs` | Pipeline interface: `IPipelineBehavior<TRequest, TResponse>` and `RequestHandlerDelegate<TResponse>` delegate |
| `src/MediatorLite/Abstractions/ISourceGeneratedMediator.cs` | Source-gen dispatch interface: 9 methods for zero-reflection routing |
| `src/MediatorLite/Abstractions/Unit.cs` | `readonly record struct Unit` — void-equivalent for generic type parameters |
| `src/MediatorLite/Abstractions/Attributes.cs` | All attributes and enums: `NotificationHandlerOrderAttribute`, `NotificationOptionsAttribute`, `BehaviorOrderAttribute`, `MediatorLoggingAttribute`, `MediatorGenerationAttribute`, `NotificationExecutionStrategy`, `NotificationErrorStrategy` |
| `src/MediatorLite/Configuration/MediatorOptions.cs` | Configuration class with properties and `AddOpenBehavior`/`AddBehavior<T>` methods |
| `src/MediatorLite/Configuration/ServiceCollectionExtensions.cs` | DI registration: `AddMediatorLite()` and `AddMediatorBehavior<T>()` extension methods |
| `src/MediatorLite/Diagnostics/MediatorDiagnostics.cs` | OpenTelemetry `ActivitySource`, `DiagnosticListener`, tag constants, event names |
| `src/MediatorLite/Internal/Mediator.cs` | Core dispatcher (592 lines) — sealed internal class implementing `IMediator` |
| `src/MediatorLite/Validation/IValidator.cs` | Validator interface: `IValidator<in TRequest>` |
| `src/MediatorLite/Validation/DataAnnotationsValidator.cs` | `DataAnnotationsValidator<TRequest>` — wraps System.ComponentModel.DataAnnotations |
| `src/MediatorLite/Validation/ValidationBehavior.cs` | `ValidationBehavior<TRequest, TResponse>` — pipeline behavior that runs validators |
| `src/MediatorLite/Validation/ValidationException.cs` | `ValidationException` — sealed exception with `IReadOnlyList<ValidationError>` |
| `src/MediatorLite/Validation/Models/ValidationError.cs` | `sealed record ValidationError(string PropertyName, string ErrorMessage, object? AttemptedValue = null)` |
| `src/MediatorLite/Validation/Models/ValidationResult.cs` | `sealed class ValidationResult` with `Success` singleton and `Failure` factory methods |

---

## Namespace Map

| Namespace | Types |
|-----------|-------|
| `MediatorLite` | `IMediator`, `IRequest<T>`, `IRequest`, `IRequestHandler<TReq,TRes>`, `IRequestHandler<TReq>`, `INotification`, `INotificationHandler<T>`, `IPipelineBehavior<TReq,TRes>`, `RequestHandlerDelegate<T>`, `ISourceGeneratedMediator`, `Unit`, all attributes, all enums, `ServiceCollectionExtensions` |
| `MediatorLite.Configuration` | `MediatorOptions` |
| `MediatorLite.Diagnostics` | `MediatorActivitySource`, `MediatorDiagnostics` |
| `MediatorLite.Internal` | `Mediator` (internal sealed) |
| `MediatorLite.Validation` | `IValidator<T>`, `DataAnnotationsValidator<T>`, `ValidationBehavior<TReq,TRes>`, `ValidationException` |
| `MediatorLite.Validation.Models` | `ValidationResult`, `ValidationError` |

---

## Source-Gen vs Reflection Dispatch Flow

### Constructor

```csharp
internal sealed class Mediator : IMediator
{
    private readonly ISourceGeneratedMediator? _sourceGeneratedMediator;

    public Mediator(IServiceProvider sp, ILogger<Mediator> logger, MediatorOptions options)
    {
        _sourceGeneratedMediator = sp.GetService<ISourceGeneratedMediator>();
        // ...
    }
}
```

If `AddGeneratedHandlers()` was called, `ISourceGeneratedMediator` is registered in DI and resolved here. Otherwise it's `null` and *all* dispatch goes through reflection.

### SendAsync Flow

```
SendAsync<TResponse>(request, ct)
│
├─ Resolve behaviors:
│   ├─ _sourceGeneratedMediator.TryResolveBehaviors(sp, requestType, responseType)
│   └─ fallback: ResolveBehaviorsFromDI(requestType, responseType)
│       └─ ConcurrentDictionary<(Type,Type), Type> _behaviorTypeCache
│       └─ typeof(IPipelineBehavior<,>).MakeGenericType(...)
│       └─ IEnumerable<IPipelineBehavior<TReq,TRes>> via DI
│
├─ If behaviors.Count == 0 (fast path):
│   ├─ _sourceGeneratedMediator.TrySendAsync<TResponse>(sp, request, ct)
│   │   └─ non-null → await and return
│   ├─ InvokeHandlerFromDI<TResponse>(requestType, request, ct)
│   │   └─ ConcurrentDictionary<(Type,Type), Type> _handlerTypeCache
│   │   └─ typeof(IRequestHandler<,>).MakeGenericType(...)
│   │   └─ sp.GetService(handlerInterfaceType)
│   │   └─ method.Invoke → ExceptionDispatchInfo on TargetInvocationException
│   └─ null → InvalidOperationException
│
└─ If behaviors.Count > 0:
    └─ ExecutePipeline(request, behaviors, ct)
        ├─ Innermost delegate:
        │   ├─ _sourceGeneratedMediator.TryInvokeHandlerAsync<TResponse>(...)
        │   └─ fallback: InvokeHandlerFromDI<TResponse>(...)
        └─ Wrap behaviors (reverse loop: i = Count-1 → 0):
            └─ InvokeBehaviorAsync → source-gen InvokeBehavior → reflection fallback
```

### Reflection Caches

Four static `ConcurrentDictionary` caches avoid repeated `MakeGenericType` / `GetCustomAttribute`:

| Cache | Key | Value |
|-------|-----|-------|
| `_handlerTypeCache` | `(requestType, responseType)` | `typeof(IRequestHandler<,>).MakeGenericType(...)` |
| `_behaviorTypeCache` | `(requestType, responseType)` | `typeof(IPipelineBehavior<,>).MakeGenericType(...)` |
| `_handlerOrderCache` | `handlerType` | `int` from `NotificationHandlerOrderAttribute.Order` |
| `_notificationOptionsCache` | `notificationType` | `(ExecutionStrategy, ErrorStrategy)?` from `NotificationOptionsAttribute` |

---

## Pipeline Execution Model

### Behavior Wrapping Order

Behaviors are wrapped in **reverse registration order** so the first-registered behavior is the outermost:

```csharp
// behaviors list: [B1, B2, B3]  (registration order)
// Wrap loop: i = 2, 1, 0
//
// After wrap:
//   handlerDelegate = B1( B2( B3( actualHandler ) ) )
//
// Execution order: B1.before → B2.before → B3.before → handler → B3.after → B2.after → B1.after
```

### Delegate Chaining

```csharp
RequestHandlerDelegate<TResponse> handlerDelegate = () => {
    // innermost: source-gen TryInvokeHandlerAsync or DI fallback
};

for (int i = behaviors.Count - 1; i >= 0; i--)
{
    var behavior = behaviors[i];
    var currentDelegate = handlerDelegate;  // capture for closure
    handlerDelegate = () => InvokeBehaviorAsync<TResponse>(behavior, requestType, request, currentDelegate, ct);
}

return await handlerDelegate();
```

### Short-Circuit Pattern

A behavior short-circuits by returning without calling `next()`:

```csharp
public async ValueTask<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
{
    if (SomeCondition)
        return cachedResult;  // short-circuit — handler never runs

    return await next();  // continue pipeline
}
```

---

## Notification Execution

### Handler Ordering

```csharp
private List<INotificationHandler<T>> OrderHandlers<T>(List<INotificationHandler<T>> handlers)
{
    if (handlers.Count <= 1) return handlers;

    // Skip-sort optimization: scan for any non-zero order
    bool needsSort = false;
    for (int i = 0; i < handlers.Count; i++)
    {
        if (GetHandlerOrder(handlers[i].GetType()) != 0)
        {
            needsSort = true;
            break;
        }
    }
    if (!needsSort) return handlers;

    return handlers.OrderBy(h => GetHandlerOrder(h.GetType())).ToList();
}
```

`GetHandlerOrder` tries source-gen first (`TryGetHandlerOrder`), then falls back to `_handlerOrderCache` with reflection.

### Notification Options Resolution

1. Source-gen: `_sourceGeneratedMediator.TryGetNotificationOptions(notificationType)` — returns `(ExecutionStrategy, ErrorStrategy)?`
2. Reflection fallback: `_notificationOptionsCache` checks `NotificationOptionsAttribute` with `OverrideGlobal`
3. Default: `(_options.NotificationExecutionStrategy, _options.NotificationErrorStrategy)`

### Sequential Execution

```csharp
foreach (var handler in handlers)
{
    try
    {
        cancellationToken.ThrowIfCancellationRequested();
        await handler.HandleAsync(notification, cancellationToken);
    }
    catch (OperationCanceledException) { throw; }  // always re-throw
    catch (Exception ex)
    {
        if (errorStrategy == StopOnFirstError) throw;
        exceptions.Add(ex);  // ContinueAndAggregate
    }
}
// throw AggregateException if any exceptions collected
```

### Parallel Execution

Uses `ArrayPool<Task>.Shared` for zero-allocation task array:

```csharp
var rentedArray = ArrayPool<Task>.Shared.Rent(count);
try
{
    for (int i = 0; i < count; i++)
        rentedArray[i] = handlers[i].HandleAsync(notification, ct).AsTask();

    await Task.WhenAll(rentedArray.AsSpan(0, count));
    // On failure: collect InnerExceptions from all faulted tasks
    // throw AggregateException
}
finally
{
    Array.Clear(rentedArray, 0, count);
    ArrayPool<Task>.Shared.Return(rentedArray);
}
```

Parallel execution always aggregates errors (collects from all faulted tasks).

### StopOnFirst Execution

Iterates handlers sequentially; returns after the **first successful** handler:

```csharp
foreach (var handler in handlers)
{
    try
    {
        cancellationToken.ThrowIfCancellationRequested();
        await handler.HandleAsync(notification, cancellationToken);
        return; // Success — stop here
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex)
    {
        if (errorStrategy == StopOnFirstError) throw;
        exceptions.Add(ex);
    }
}
// If all failed with ContinueAndAggregate: throw AggregateException("All notification handlers for X threw exceptions.")
```
