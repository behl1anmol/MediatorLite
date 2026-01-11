# Migration from MediatR

This guide helps you migrate from MediatR to MediatorLite.

## Interface Mapping

| MediatR | MediatorLite | Notes |
|---------|--------------|-------|
| `IRequest<TResponse>` | `IRequest<TResponse>` | Same |
| `IRequest` | `IRequest` | Same |
| `IRequestHandler<TRequest, TResponse>` | `IRequestHandler<TRequest, TResponse>` | Same signature |
| `INotification` | `INotification` | Same |
| `INotificationHandler<T>` | `INotificationHandler<T>` | Same |
| `IPipelineBehavior<TRequest, TResponse>` | `IPipelineBehavior<TRequest, TResponse>` | Return type changes |
| `Unit` | `Unit` | Same concept |

## Key Differences

### 1. Return Type: Task → ValueTask

MediatorLite uses `ValueTask<T>` for better performance.

**MediatR:**
```csharp
public class MyHandler : IRequestHandler<MyQuery, Result>
{
    public Task<Result> Handle(MyQuery request, CancellationToken ct)
    {
        return Task.FromResult(new Result());
    }
}
```

**MediatorLite:**
```csharp
public class MyHandler : IRequestHandler<MyQuery, Result>
{
    public ValueTask<Result> HandleAsync(MyQuery request, CancellationToken ct = default)
    {
        return ValueTask.FromResult(new Result());
    }
}
```

### 2. Method Name: Handle → HandleAsync

| MediatR | MediatorLite |
|---------|--------------|
| `Handle()` | `HandleAsync()` |
| `Send()` | `SendAsync()` |
| `Publish()` | `PublishAsync()` |

### 3. Registration

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

### 4. Pipeline Behaviors

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

Use find-and-replace:
- `Task<` → `ValueTask<`
- `Handle(` → `HandleAsync(`
- `: IRequestHandler<` stays the same
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
