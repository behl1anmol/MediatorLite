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

**When using source-generated registration, behaviors are automatically discovered and registered. Do NOT use `MediatorOptions.AddOpenBehavior()` — it's not needed.**

If your behaviors are in the same project as the source generator, `AddGeneratedHandlers()` will discover and register them automatically with ordering from `[BehaviorOrder]`:

```csharp
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // Discovers and registers ALL behaviors with [BehaviorOrder] ordering
    .AddMediatorLite();       // No need to call options.AddOpenBehavior()
```

To register only behaviors from the source generator:

```csharp
services
    .AddGeneratedBehaviors()  // Only pipeline behaviors
    .AddMediatorLite();
```

**Important:** The source generator discovers both open generic behaviors (e.g., `LoggingBehavior<,>`) and closed behaviors (e.g., `CreateOrderValidationBehavior`). They are registered directly in DI and ordered by `[BehaviorOrder]`.

### Manual Registration (Deprecated)

> ⚠️ **Deprecated in v2:** Manual registration uses reflection fallback and does not respect `[BehaviorOrder]`.

When NOT using source-generated registration, you have two options:

#### Option 1: Via MediatorOptions

Register open generic behaviors through `MediatorOptions`. This automatically adds them to DI:

```csharp
services.AddMediatorLite(options =>
{
    options.AddOpenBehavior(typeof(LoggingBehavior<,>));
    options.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
```

Register closed behaviors:

```csharp
services.AddMediatorLite(options =>
{
    options.AddBehavior<CreateOrderAuthorizationBehavior>();
});
```

#### Option 2: Direct DI Registration

You can also register behaviors directly with the DI container:

```csharp
// Open generic - applies to all requests
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// Closed type - applies to specific request only
services.AddTransient<IPipelineBehavior<CreateOrder, OrderResult>, CreateOrderLoggingBehavior>();

services.AddMediatorLite();
```

### Summary: Source-Gen vs Manual Registration

| Method | When to Use | Behavior Ordering |
|--------|-------------|-------------------|
| **Source-Generated (v2)** | Required for v2 | `[BehaviorOrder]` attribute |
| **Manual via Options** | Deprecated | Registration order |
| **Manual via DI** | Deprecated | Registration order |

**Key Rule:** Always use `AddGeneratedHandlers()` in v2 to get O(1) dispatch and `[BehaviorOrder]` support.

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

```csharp
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            if (!result.IsValid)
            {
                throw new ValidationException(result.Errors);
            }
        }

        return await next();
    }
}
```

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
