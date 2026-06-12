# Pipeline Behaviors

Pipeline behaviors allow you to add cross-cutting concerns to your request handling pipeline.

## v2 Changes

In v2, behavior execution order is controlled by the **`[BehaviorOrder]` attribute** at compile time:

```csharp
[BehaviorOrder(1)]  // Executes first
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }

[BehaviorOrder(2)]  // Executes second
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }
```

> ⚠️ **v2 Change:** DI registration order no longer determines behavior execution order. Use `[BehaviorOrder]` instead.

## Open vs Closed Behaviors

**Open behavior** = an open generic type definition (e.g., `LoggingBehavior<,>`) that can be applied to any request/response pair. It is resolved by DI for each concrete request at runtime.

**Closed behavior** = a concrete type (non-generic or fully closed generic) that targets a specific request/response pair.

Example request/response:

```csharp
public sealed record CreateOrder(string ProductId) : IRequest<OrderResult>;

public sealed record OrderResult(Guid OrderId);
```

### Open behavior example

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling {RequestType}", typeof(TRequest).Name);

        var stopwatch = Stopwatch.StartNew();
        var response = await next(); // Call the next behavior or handler
        stopwatch.Stop();

        _logger.LogInformation("Handled {RequestType} in {ElapsedMs}ms",
            typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);

        return response;
    }
}
```

### Closed behavior example

```csharp
public sealed class CreateOrderLoggingBehavior
    : IPipelineBehavior<CreateOrder, OrderResult>
{
    public async ValueTask<OrderResult> HandleAsync(
        CreateOrder request,
        RequestHandlerDelegate<OrderResult> next,
        CancellationToken cancellationToken = default)
    {
        return await next();
    }
}
```

## Registering Behaviors

### Source-Generated Registration (Required for v2)

**When using source-generated registration, behaviors are automatically discovered and registered — no manual `AddOpenBehavior(...)` call is required or supported.**

If your behaviors are in the same project as the source generator, `AddGeneratedHandlers()` will discover and register them automatically with ordering from `[BehaviorOrder]`:

```csharp
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // Discovers and registers ALL behaviors with [BehaviorOrder] ordering
    .AddMediatorLite();       // Takes no arguments; mediator is always registered as Transient
```

To register only behaviors from the source generator:

```csharp
services
    .AddGeneratedBehaviors()  // Only pipeline behaviors
    .AddMediatorLite();
```

**Important:** The source generator discovers both open generic behaviors (e.g., `LoggingBehavior<,>`) and closed behaviors (e.g., `CreateOrderValidationBehavior`). They are registered directly in DI and ordered by `[BehaviorOrder]`.

### Manual Registration (Not Supported)

> ⚠️ **Not supported in v2:** There is no reflection fallback, so manually registered behaviors are never dispatched without `AddGeneratedHandlers()`. Worse, hand-registering behaviors **alongside** source generation double-registers them — the generator already discovered and registered every open-generic and closed behavior. Never call `services.AddTransient(typeof(IPipelineBehavior<,>), ...)` in a source-generated consumer.

**Key Rule:** `AddGeneratedHandlers()` is the only registration path in v2; behavior ordering is controlled exclusively by `[BehaviorOrder]`.

## Execution Order (v2)

In v2, behaviors execute in `[BehaviorOrder]` attribute order (lowest number first):

```
Request -> [BehaviorOrder(1)] LoggingBehavior -> [BehaviorOrder(2)] ValidationBehavior -> Handler -> ValidationBehavior -> LoggingBehavior -> Response
```

### Using BehaviorOrderAttribute

```csharp
[BehaviorOrder(1)]  // Executes first
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // ...
}

[BehaviorOrder(2)]  // Executes second
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // ...
}

[BehaviorOrder(100)]  // Executes last (just before handler)
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // ...
}
```

Behaviors without `[BehaviorOrder]` default to order `0`.

> **Tip:** Use gaps in ordering (1, 10, 100) to allow inserting new behaviors without renumbering.
```

## Common Behavior Patterns

### Validation Behavior

You do **not** need to write your own validation behavior. MediatorLite provides
FluentValidation integration out of the box: add the `MediatorLite.FluentValidation` package,
write `AbstractValidator<T>` validators, and the source generator wires
`FluentValidationBehavior<,>` as the outermost behavior automatically. See
[Validation](validation.md).

### Transaction Behavior

```csharp
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IDbContext _dbContext;

    public TransactionBehavior(IDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.BeginTransactionAsync(cancellationToken);

        try
        {
            var response = await next();
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
```

## Short-Circuiting

Behaviors can short-circuit the pipeline by not calling `next()`:

```csharp
public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ICache _cache;

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GenerateCacheKey(request);

        if (_cache.TryGet<TResponse>(cacheKey, out var cached))
        {
            return cached!; // Don't call next(), return cached value
        }

        var response = await next();
        _cache.Set(cacheKey, response);
        return response;
    }
}
