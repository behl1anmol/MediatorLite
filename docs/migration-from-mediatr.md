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

### 3. Method Name: Handle -> HandleAsync

| MediatR | MediatorLite |
|---------|--------------|
| `Handle()` | `HandleAsync()` |
| `Send()` | `SendAsync()` |
| `Publish()` | `PublishAsync()` |

### 4. Registration

**MediatR** uses runtime assembly scanning:
```csharp
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
```

**MediatorLite** uses compile-time source generation:
```csharp
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // Source-generated: zero reflection at startup
    .AddMediatorLite(options =>
    {
        options.AddOpenBehavior(typeof(LoggingBehavior<,>));
    });
```

Or register handlers manually with standard DI:
```csharp
services.AddTransient<IRequestHandler<MyQuery, Result>, MyQueryHandler>();
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddMediatorLite();
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
<PackageReference Include="MediatorLite.SourceGeneration" Version="1.0.0" />
```

If you keep requests/notifications in a separate shared project, use this there instead:

```xml
<PackageReference Include="MediatorLite.Abstractions" Version="1.0.0" />
```

Notes:
- Installing `MediatorLite` pulls `MediatorLite.Abstractions` transitively.
- Installing only `MediatorLite.SourceGeneration` does not provide runtime mediator contracts.

### Step 2: Update Using Statements

```csharp
// Remove
using MediatR;

// Add
using MediatorLite;
```

### Step 3: Update Handlers

For handlers, update the return type and method name:
- `Task<T> Handle(` -> `ValueTask<T> HandleAsync(`
- `Task Handle(` -> `ValueTask HandleAsync(`
- `Task.FromResult(x)` -> `ValueTask.FromResult(x)`
- `Task.CompletedTask` -> `ValueTask.CompletedTask`
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

Replace MediatR's runtime assembly scanning with source-generated registration:

```csharp
// Before (MediatR)
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...));

// After (MediatorLite - source generation)
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // Compile-time handler discovery
    .AddMediatorLite();

// Or: manual DI registration
services.AddTransient<IRequestHandler<MyQuery, MyResult>, MyQueryHandler>();
services.AddMediatorLite();
```

## Source Generation

MediatorLite includes a Roslyn source generator (`MediatorLite.SourceGeneration`) that discovers handlers, notification handlers, and pipeline behaviors at compile time.

### How It Works

The source generator scans your project for types implementing:
- `IRequestHandler<TRequest, TResponse>`
- `INotificationHandler<TNotification>`
- `IPipelineBehavior<TRequest, TResponse>`

It generates a `MediatorLiteRegistration` class with extension methods to register all discovered types with the DI container.

### Registration Methods

```csharp
using MediatorLite.Generated;

// Register everything at once
services.AddGeneratedHandlers();

// Or register specific categories
services.AddGeneratedRequestHandlers();        // Only request handlers
services.AddGeneratedNotificationHandlers();   // Only notification handlers
services.AddGeneratedBehaviors();              // Only pipeline behaviors
```

### Zero-Reflection Dispatch

`AddGeneratedHandlers()` also registers a `SourceGeneratedMediator` that implements `ISourceGeneratedMediator`. This enables the mediator to dispatch requests to handlers using direct typed method calls instead of reflection-based `MethodInfo.Invoke()`.

The source-generated mediator provides:
- **Typed request dispatch** - Direct handler invocation without `MakeGenericType`/`MethodInfo.Invoke`
- **Typed behavior resolution** - Resolve `IPipelineBehavior<TRequest, TResponse>` without reflection
- **Handler ordering** - Compile-time lookup of `[NotificationHandlerOrder]` attributes
- **Notification options** - Compile-time lookup of `[NotificationOptions]` attributes

### Excluding Types

Use `[MediatorGeneration(Skip = true)]` to exclude a type from source generation:

```csharp
[MediatorGeneration(Skip = true)]
public class TestHandler : IRequestHandler<TestQuery, string>
{
    // Not registered by AddGeneratedHandlers()
}
```

### Diagnostics

```csharp
Console.WriteLine($"Request handlers: {MediatorLiteRegistration.RequestHandlerCount}");
Console.WriteLine($"Notification handlers: {MediatorLiteRegistration.NotificationHandlerCount}");
Console.WriteLine($"Behaviors: {MediatorLiteRegistration.BehaviorCount}");
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

Find: `\.Send\(` -> Replace: `.SendAsync(`
Find: `\.Publish\(` -> Replace: `.PublishAsync(`

## Notification Execution Strategies

MediatorLite provides enhanced control over notification execution that differs from MediatR's default behavior.

### Strategy Options

| Strategy | MediatR | MediatorLite |
|----------|---------|--------------|
| Sequential execution | Default (no option) | `NotificationExecutionStrategy.Sequential` |
| Parallel execution | Not built-in | `NotificationExecutionStrategy.Parallel` |
| Stop on first success | Not available | `NotificationExecutionStrategy.StopOnFirst` |

### Error Handling Strategies

| Error Strategy | Behavior |
|----------------|----------|
| `StopOnFirstError` | Stop execution and throw immediately (MediatR's behavior) |
| `ContinueAndAggregate` | Execute all handlers, aggregate exceptions |

### Strategy-Specific Behavior

MediatorLite applies error strategies based on the execution mode:

| Execution Strategy | Error Strategy Effect |
|--------------------|----------------------|
| **Sequential** | Both strategies work as expected |
| **Parallel** | Error strategy ignored - always aggregates* |
| **StopOnFirst** | Both strategies work as expected |

> *Parallel mode always aggregates exceptions because concurrent tasks cannot be cancelled mid-execution. This is by design.

### Configuration Example

```csharp
services.AddMediatorLite(options =>
{
    // MediatR-like behavior (sequential, stop on first error)
    options.NotificationExecutionStrategy = NotificationExecutionStrategy.Sequential;
    options.NotificationErrorStrategy = NotificationErrorStrategy.StopOnFirstError;

    // Or: More resilient production setup
    options.NotificationExecutionStrategy = NotificationExecutionStrategy.Parallel;
    options.NotificationErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate;
});
```

### Per-Notification Override

```csharp
[NotificationOptions(
    ExecutionStrategy = NotificationExecutionStrategy.StopOnFirst,
    ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate,
    OverrideGlobal = true)]
public record FallbackNotification(string Message) : INotification;
```

See [Notifications documentation](notifications.md) for detailed strategy behavior.

## Features Not Available in MediatorLite v1.0

| MediatR Feature | MediatorLite Status |
|-----------------|---------------------|
| `IStreamRequest<T>` | Not in v1.0 |
| `CreateScope()` | Not needed (use DI scopes) |
| `ServiceFactory` | Not in v1.0 (use DI directly) |
