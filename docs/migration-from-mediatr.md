# Migration from MediatR

This guide helps you migrate from MediatR to MediatorLite v2.

## v2 Key Changes

MediatorLite v2 introduces a **source-generation-first architecture**:

| Aspect | MediatR | MediatorLite v2 |
|--------|---------|-----------------|
| Handler dispatch | Reflection | O(1) source-generated switch |
| Behavior ordering | Registration order | `[BehaviorOrder]` attribute |
| Notification strategies | Runtime configuration | `[NotificationExecution]` / `[NotificationError]` attributes (with assembly-level defaults) |
| Handler ordering | N/A | `[NotificationHandlerOrder]` attribute |
| Required registration | `AddMediatR()` | `AddGeneratedHandlers()` + `AddMediatorLite()` |
| Logging on/off | `cfg.AddOpenBehavior(typeof(LoggingBehavior<,>))` | Compile-time: `[assembly: DisableMediatorLogging]` (opt-out) |
| Tracing on/off | Manual | Compile-time: `[assembly: DisableMediatorTracing]` (opt-out) |
| Log level | MEL filters | MEL filters (`AddFilter("MediatorLite.IMediator", ...)`) |
| Mediator lifetime | Configurable | Always `Transient` |

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

**MediatorLite v2** uses compile-time source generation. **You must call `AddGeneratedHandlers()` before `AddMediatorLite()`:**
```csharp
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // MUST be called first — O(1) dispatch + [BehaviorOrder] support
    .AddMediatorLite();       // Takes no arguments; mediator is always registered as Transient
```

Built-in logging and tracing are on by default. Opt out at compile time with assembly-level attributes (both no-arg, in the `MediatorLite` namespace):

```csharp
[assembly: DisableMediatorLogging]
[assembly: DisableMediatorTracing]
```

The log level is controlled through standard `Microsoft.Extensions.Logging` configuration (generated code logs at `Debug` under the `MediatorLite.IMediator` category).

> ⚠️ **v2 Change:** `options.AddOpenBehavior()` is no longer needed — behaviors are auto-discovered and ordered by `[BehaviorOrder]`. The entire `MediatorOptions` configure lambda has been **removed**.

Or register handlers manually with standard DI (deprecated):
```csharp
services.AddTransient<IRequestHandler<MyQuery, Result>, MyQueryHandler>();
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddMediatorLite();  // Falls back to reflection (deprecated)
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

**MediatorLite v2** — use `[BehaviorOrder]` to control execution order:
```csharp
[BehaviorOrder(1)]  // Executes first
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

> ⚠️ **v2 Change:** Behavior execution order is determined by `[BehaviorOrder]` attribute, not DI registration order.

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

Replace MediatR's runtime assembly scanning with v2 source-generated registration:

```csharp
// Before (MediatR)
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...));

// After (MediatorLite v2)
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // MUST be called first for O(1 dispatch
    .AddMediatorLite();
```

### Step 6: Add Compile-Time Attributes

**Behavior ordering** — add `[BehaviorOrder]` to your behaviors:

```csharp
[BehaviorOrder(1)]  // LoggingBehavior runs first
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }

[BehaviorOrder(2)]  // ValidationBehavior runs second
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }
```

**Notification strategies** — add `[NotificationExecution]` and/or `[NotificationError]` to notification types:

```csharp
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record UserCreatedNotification(int UserId) : INotification;
```

Or declare assembly-wide defaults once and override per notification as needed:

```csharp
[assembly: DefaultNotificationExecution(NotificationExecutionStrategy.Parallel)]
[assembly: DefaultNotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
```

## Source Generation (v2)

MediatorLite v2 requires the Roslyn source generator (`MediatorLite.SourceGeneration`) for O(1) dispatch.

### How It Works

The source generator scans your project for types implementing:
- `IRequestHandler<TRequest, TResponse>`
- `INotificationHandler<TNotification>`
- `IPipelineBehavior<TRequest, TResponse>`

It generates:
- O(1) switch expressions for handler dispatch (no dictionary lookups)
- Behavior ordering based on `[BehaviorOrder]` attributes
- Notification strategy lookup based on `[NotificationExecution]` / `[NotificationError]` attributes (with assembly-level defaults merged in at compile time)
- Handler ordering based on `[NotificationHandlerOrder]` attributes

### Registration Methods

```csharp
using MediatorLite.Generated;

// Register everything at once (MUST be called before AddMediatorLite)
services.AddGeneratedHandlers();

// Or register specific categories
services.AddGeneratedRequestHandlers();        // Only request handlers
services.AddGeneratedNotificationHandlers();   // Only notification handlers
services.AddGeneratedBehaviors();              // Only pipeline behaviors
```

### O(1) Dispatch

`AddGeneratedHandlers()` registers a `SourceGeneratedMediator` that implements `ISourceGeneratedMediator`. This provides O(1) dispatch via generated switch expressions:

```csharp
// Generated code (simplified)
public ValueTask<TResponse> SendAsync<TRequest, TResponse>(TRequest request, ...) =>
    request switch
    {
        GetUserQuery q => HandleGetUserQuery(q, ct),
        CreateOrderCommand c => HandleCreateOrderCommand(c, ct),
        // ... O(1) lookup, not dictionary-based
    };
```

The source-generated mediator provides:
- **O(1) request dispatch** — Generated switch expression instead of `MakeGenericType`/`MethodInfo.Invoke`
- **Typed behavior resolution** — Resolve `IPipelineBehavior<TRequest, TResponse>` without reflection
- **Handler ordering** — Compile-time lookup of `[NotificationHandlerOrder]` attributes
- **Notification strategies** — Compile-time resolution of `[NotificationExecution]` / `[NotificationError]` attributes, merged with `[assembly: DefaultNotificationExecution]` / `[assembly: DefaultNotificationError]`

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

## Notification Execution Strategies (v2)

MediatorLite v2 provides enhanced control over notification execution via compile-time attributes.

### Strategy Options

| Strategy | MediatR | MediatorLite v2 |
|----------|---------|-----------------|
| Sequential execution | Default (no option) | `[NotificationExecution(NotificationExecutionStrategy.Sequential)]` (or library default) |
| Parallel execution | Not built-in | `[NotificationExecution(NotificationExecutionStrategy.Parallel)]` |
| Stop on first success | Not available | `[NotificationExecution(NotificationExecutionStrategy.StopOnFirst)]` |

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

### Configuration Example (v2)

Configure via attributes on your notification types:

```csharp
// MediatR-like behavior (sequential, stop on first error) - matches library defaults, so no attributes needed
public record OrderPlacedNotification(int OrderId) : INotification;

// More resilient production setup
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record UserCreatedNotification(int UserId) : INotification;
```

> ⚠️ **v2 Change:** `MediatorOptions` is gone, `AddMediatorLite` no longer accepts a configure lambda, and the old runtime notification strategy properties plus the `[NotificationOptions]` attribute have been removed. Use `[NotificationExecution]` / `[NotificationError]` (or their `[assembly: Default...]` counterparts).

### Per-Notification Handler Ordering

```csharp
[NotificationHandlerOrder(1)]  // Executes first
public class FirstHandler : INotificationHandler<MyNotification> { }

[NotificationHandlerOrder(2)]  // Executes second
public class SecondHandler : INotificationHandler<MyNotification> { }
```

See [Notifications documentation](notifications.md) for detailed strategy behavior.

## Features Not Available in MediatorLite v2

| MediatR Feature | MediatorLite v2 Status |
|-----------------|------------------------|
| `IStreamRequest<T>` | Not in v2 |
| `CreateScope()` | Not needed (use DI scopes) |
| `ServiceFactory` | Not in v2 (use DI directly) |
| Runtime behavior ordering | Replaced by `[BehaviorOrder]` attribute |
| Runtime notification strategy | Replaced by `[NotificationExecution]` / `[NotificationError]` attributes (with `[assembly: Default...]` for defaults) |
