# Migration from MediatR

This guide helps you migrate from MediatR to MediatorLite.

## Interface Mapping

| MediatR | MediatorLite | Notes |
|---------|--------------|-------|
| `IRequest<TResponse>` | `IRequest<TResponse>` | Same |
| `IRequest` | `IRequest` | Same |
| `IRequestHandler<TRequest, TResponse>` | `IRequestHandler<TRequest, TResponse>` | Handler returns `ValueTask<T>` for performance |
| `INotification` | `INotification` | Same |
| `INotificationHandler<T>` | `INotificationHandler<T>` | Handler returns `ValueTask` |
| `IPipelineBehavior<TRequest, TResponse>` | `IPipelineBehavior<TRequest, TResponse>` | Behavior returns `ValueTask<T>` |
| `Unit` | `Unit` | Same concept |
| `IMediator.Send<T>()` returns `Task<T>` | `IMediator.SendAsync<T>()` returns `Task<T>` | **Same return type for consumer ergonomics** |
| `IMediator.Publish()` returns `Task` | `IMediator.PublishAsync()` returns `Task` | **Same return type for consumer ergonomics** |

## Key Differences

### 1. Public API: Task-based for Consumer Ergonomics

MediatorLite's `IMediator` interface returns `Task<T>` and `Task` for maximum consumer ergonomics, enabling natural parallel execution patterns:

```csharp
// MediatorLite supports natural parallel execution
var task1 = _mediator.SendAsync(new GetUserQuery(1));
var task2 = _mediator.SendAsync(new GetOrderQuery(1));
await Task.WhenAll(task1, task2);  // Works naturally!
```

### 2. Handler Internals: ValueTask for Performance

Internally, handlers use `ValueTask<T>` for better performance on synchronous completion paths:

**MediatR:**
```csharp
public class MyHandler : IRequestHandler<MyQuery, Result>
{
    public Task<Result> Handle(MyQuery request, CancellationToken ct)
    {
        return Task.FromResult(new Result());  // Allocates Task
    }
}
```

**MediatorLite:**
```csharp
public class MyHandler : IRequestHandler<MyQuery, Result>
{
    public ValueTask<Result> HandleAsync(MyQuery request, CancellationToken ct = default)
    {
        return ValueTask.FromResult(new Result());  // Zero allocation for sync completion
    }
}
```

### 3. Method Name: Handle → HandleAsync

| MediatR | MediatorLite |
|---------|--------------|
| `Handle()` | `HandleAsync()` |
| `Send()` | `SendAsync()` |
| `Publish()` | `PublishAsync()` |

### 4. Registration

**MediatR:**
```csharp
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
```

**MediatorLite:**
```csharp
services.AddMediatorLite(options =>
{
    options.RegisterHandlersFromAssemblyContaining<Program>();
    options.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
```

### 5. Pipeline Behaviors

**MediatR:**
```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        return await next();
    }
}
```

**MediatorLite:**
```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct = default)
    {
        return await next();
    }
}
```

## Migration Steps

### Step 1: Update Package Reference

```xml
<!-- Remove -->
<PackageReference Include="MediatR" Version="..." />

<!-- Add -->
<PackageReference Include="MediatorLite" Version="1.0.0" />
```

### Step 2: Update Using Statements

```csharp
// Remove
using MediatR;

// Add
using MediatorLite;
```

### Step 3: Update Handlers

For handlers, update the return type and method name:
- `Task<T> Handle(` → `ValueTask<T> HandleAsync(`
- `Task Handle(` → `ValueTask HandleAsync(`
- `Task.FromResult(x)` → `ValueTask.FromResult(x)`
- `Task.CompletedTask` → `ValueTask.CompletedTask`
- Add `= default` to CancellationToken parameters

### Step 4: Update Mediator Calls

```csharp
// Before
await _mediator.Send(query);
await _mediator.Publish(notification);

// After
await _mediator.SendAsync(query);
await _mediator.PublishAsync(notification);
```

### Step 5: Update Registration

```csharp
// Before
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...));

// After
services.AddMediatorLite(options => options.RegisterHandlersFromAssembly(...));
```

## Source Generator (Zero-Reflection Registration)

MediatorLite includes a source generator for compile-time handler discovery:

```csharp
// Instead of runtime reflection scanning
services.AddMediatorLite();

// Use source-generated registrations
services.AddMediatorLite();
services.AddGeneratedHandlers();  // Zero reflection at startup!
```

## Regex for Bulk Migration

### Handler Method Signature

Find:
```regex
public (async )?Task<(.+?)> Handle\((.+?) request, CancellationToken (\w+)\)
```

Replace:
```
public $1ValueTask<$2> HandleAsync($3 request, CancellationToken $4 = default)
```

### Mediator Calls

Find: `\.Send\(` → Replace: `.SendAsync(`
Find: `\.Publish\(` → Replace: `.PublishAsync(`

## Features Not Available in MediatorLite v1.0

| MediatR Feature | MediatorLite Status |
|-----------------|---------------------|
| `IStreamRequest<T>` | Not in v1.0 |
| `CreateScope()` | Not needed (use DI scopes) |
| `ServiceFactory` | Not in v1.0 (use DI directly) |
