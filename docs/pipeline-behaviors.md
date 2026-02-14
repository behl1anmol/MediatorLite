# Pipeline Behaviors

Pipeline behaviors allow you to add cross-cutting concerns to your request handling pipeline.

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

### Source-Generated Registration

If your behaviors are in the same project as the source generator, `AddGeneratedHandlers()` will discover and register them automatically:

```csharp
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // Discovers and registers behaviors automatically
    .AddMediatorLite();
```

To register only behaviors from the source generator:

```csharp
services
    .AddGeneratedBehaviors()  // Only pipeline behaviors
    .AddMediatorLite();
```

### Manual Registration via MediatorOptions

Register open generic behaviors through `MediatorOptions`:

```csharp
services.AddMediatorLite(options =>
{
    options.AddOpenBehavior(typeof(LoggingBehavior<,>));
    options.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
```

This registers them as `IPipelineBehavior<,>` in DI automatically.

### Manual Registration via DI

You can also register behaviors directly with the DI container:

```csharp
// Open generic - applies to all requests
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// Closed type - applies to specific request only
services.AddTransient<IPipelineBehavior<CreateOrder, OrderResult>, CreateOrderLoggingBehavior>();
```

### Closed Type Registration via Options

```csharp
options.AddBehavior<AuthorizationBehavior>();
```

### Convenience Extension Method

```csharp
services.AddMediatorBehavior<LoggingBehavior<,>>();
```

## Execution Order

Behaviors execute in registration order (first registered = first executed):

```
Request -> LoggingBehavior -> ValidationBehavior -> Handler -> ValidationBehavior -> LoggingBehavior -> Response
```

### Using BehaviorOrderAttribute

```csharp
[BehaviorOrder(1)] // Executes first
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }

[BehaviorOrder(2)] // Executes second
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }
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
