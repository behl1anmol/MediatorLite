
# Pipeline Behavior Conventions

`IPipelineBehavior<TRequest, TResponse>` wraps the handler for a single
request. The source generator discovers them, orders them, and emits a single
inline pipeline per request type.

## Rule 1 — Behavior signature

Behaviors return `ValueTask<TResponse>`, take a `RequestHandlerDelegate<TResponse>`
named `next`, and a `CancellationToken`. The contract is fixed:

```45:59:src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// ...
    ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default);
}
```

Do not introduce a `Task<T>` overload. Do not add a second `next` parameter.

## Rule 2 — Ordering via `[BehaviorOrder]`

Behaviors execute in `[BehaviorOrder(int)]` order, **lowest first** (outer
wraps inner). Behaviors without the attribute default to order `0`.

```342:366:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
[BehaviorOrder(1)]
public class AddOneBehavior : IPipelineBehavior<ComputeValueQuery, int>
{
    public async ValueTask<int> HandleAsync(
        ComputeValueQuery request,
        RequestHandlerDelegate<int> next,
        CancellationToken cancellationToken = default)
    {
        var result = await next();
        return result + 1;
    }
}

[BehaviorOrder(2)]
public class MultiplyByTwoBehavior : IPipelineBehavior<ComputeValueQuery, int>
{
    public async ValueTask<int> HandleAsync(
        ComputeValueQuery request,
        RequestHandlerDelegate<int> next,
        CancellationToken cancellationToken = default)
    {
        var result = await next();
        return result * 2;
    }
}
```

The generator **emits validation behaviors first** for any request type with a
validator; ordinary `[BehaviorOrder]` values apply only among non-validation
behaviors. Do not try to inject a custom behavior "before validation" — there
is no slot for it.

## Rule 3 — Short-circuit by not awaiting `next()`

A behavior may skip the handler and remainder of the pipeline by returning
without calling `next()`. This is the supported cancel/guard pattern:

```386:398:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
[BehaviorOrder(1)]
public class ShortCircuitBehavior : IPipelineBehavior<ShortCircuitQuery, Unit>
{
    public static bool Executed = false;
    public ValueTask<Unit> HandleAsync(
        ShortCircuitQuery request,
        RequestHandlerDelegate<Unit> next,
        CancellationToken cancellationToken = default)
    {
        Executed = true;
        return Unit.CompletedTask;
    }
}
```

Don't throw to short-circuit if the intent is "skip handler, return default" —
return `Unit.CompletedTask` or `ValueTask.FromResult(...)` instead.

## Rule 4 — Open generics are auto-discovered

Behaviors declared as `SomeBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>`
with a `where TRequest : IRequest<TResponse>` constraint are discovered and
registered by the source generator and applied to every request type.
**Never** call `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(...))`
in source-generated consumer code — it will double-register.

Closed behaviors (bound to a specific request type, e.g.
`PlaceOrderAuthorizationBehavior : IPipelineBehavior<PlaceOrderCommand, OrderResult>`)
are also auto-registered.
