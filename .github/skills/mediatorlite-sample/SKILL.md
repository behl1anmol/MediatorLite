---
name: mediatorlite-sample
description: >
  Knowledge skill for the MediatorLite manual DI registration sample project (samples/MediatorLite.Sample).
  Use this skill whenever working on the reflection-based sample, adding new demo handlers or notifications,
  modifying manual DI registration patterns, or understanding how MediatorLite works without source generation.
  Also use when creating new examples showing manual handler registration, when the user mentions "manual sample",
  "reflection sample", "sample project", "demo handlers", or asks how to wire up MediatorLite without the
  source generator. Even if the user just says "show me how to register handlers manually", use this skill.
---

# MediatorLite Manual DI Sample

## Project Purpose

The `samples/MediatorLite.Sample/` project demonstrates MediatorLite with **manual (reflection-based) DI registration** — no source generator involved. It is a standalone console application that shows how to:

- Register request handlers, notification handlers, and pipeline behaviors explicitly in DI
- Configure `MediatorOptions` (logging, tracing)
- Send queries that return data, commands that return `Unit`, and commands with DataAnnotations
- Publish notifications to multiple ordered handlers
- Use an open generic pipeline behavior for cross-cutting concerns

Because there is no source generator, the mediator uses the **reflection fallback** dispatch path internally (`MakeGenericType` + `ConcurrentDictionary` caching).

## Project Structure

| Path | Purpose |
|------|---------|
| `MediatorLite.Sample.csproj` | Console app targeting net10.0; references `MediatorLite`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Console` |
| `Program.cs` | Top-level entry point — DI setup, handler/behavior registration, mediator usage examples |
| `Requests/GetUserQuery.cs` | Query record: `GetUserQuery(int Id) : IRequest<User>` |
| `Requests/User.cs` | Response record: `User(int Id, string Name, string Email)` |
| `Requests/DeleteUserCommand.cs` | Void command record: `DeleteUserCommand(int Id) : IRequest` (returns `Unit`) |
| `Requests/CreateOrderCommand.cs` | Command with DataAnnotations: `CreateOrderCommand(string ProductName, int Quantity, decimal Price) : IRequest<int>` — uses `[Required]`, `[Range]` |
| `Handlers/GetUserQueryHandler.cs` | Handles `GetUserQuery` → returns a simulated `User` |
| `Handlers/DeleteUserCommandHandler.cs` | Handles `DeleteUserCommand` → logs deletion, returns `Unit` via `IRequestHandler<DeleteUserCommand>` (the void shorthand) |
| `Handlers/CreateOrderCommandHandler.cs` | Handles `CreateOrderCommand` → logs order details, returns a random order ID |
| `Notifications/UserCreatedNotification.cs` | Notification record: `UserCreatedNotification(int UserId, string Email) : INotification` |
| `Notifications/SendWelcomeEmailHandler.cs` | Notification handler with `[NotificationHandlerOrder(1)]` — logs sending a welcome email |
| `Notifications/CreateAuditLogHandler.cs` | Notification handler with `[NotificationHandlerOrder(2)]` — logs creating an audit entry |
| `Behaviors/LoggingBehavior.cs` | Open generic `IPipelineBehavior<TRequest, TResponse>` — logs request name + elapsed time via `Stopwatch` |

## DI Registration Pattern

The sample uses top-level statements in `Program.cs`. The full registration sequence:

```csharp
var services = new ServiceCollection();

// 1. Logging
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// 2. Register request handlers manually (one line per handler)
services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserQueryHandler>();
services.AddTransient<IRequestHandler<DeleteUserCommand, Unit>, DeleteUserCommandHandler>();
services.AddTransient<IRequestHandler<CreateOrderCommand, int>, CreateOrderCommandHandler>();

// 3. Register notification handlers
services.AddTransient<INotificationHandler<UserCreatedNotification>, SendWelcomeEmailHandler>();
services.AddTransient<INotificationHandler<UserCreatedNotification>, CreateAuditLogHandler>();

// 4. Register open generic behaviors
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// 5. Add MediatorLite with options
services.AddMediatorLite(options =>
{
    options.EnableBuiltInLogging = true;
    options.EnableTracing = true;
});

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();
```

Key points about registration order:
- Handlers and behaviors must be registered **before** calling `AddMediatorLite()`.
- Open generic behaviors are registered via `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>))` — not via `MediatorOptions.AddOpenBehavior`.
- Without `ISourceGeneratedMediator` in DI, the mediator automatically uses reflection-based dispatch.

## Demonstrated Features

### 1. Query with Response — `GetUserQuery → User`

A positional record `GetUserQuery(int Id)` implementing `IRequest<User>`. The handler returns a hardcoded `User` via `ValueTask.FromResult`. Demonstrates the basic request/response pattern.

### 2. Void Command — `DeleteUserCommand`

A positional record `DeleteUserCommand(int Id)` implementing `IRequest` (the `Unit` shorthand). The handler implements `IRequestHandler<DeleteUserCommand>` (single type parameter), which is the convenience interface that wraps `ValueTask HandleAsync(…)` → `Unit.Value`. Uses constructor-injected `ILogger`.

### 3. Command with DataAnnotations — `CreateOrderCommand`

```csharp
public record CreateOrderCommand(
    [property: Required] string ProductName,
    [property: Range(1, 100)] int Quantity,
    [property: Range(0.01, 10000)] decimal Price) : IRequest<int>;
```

Shows how to place DataAnnotations validation attributes on positional record parameters using the `[property:]` target. Note: the sample does not register `ValidationBehavior` — the annotations are present for illustration but not enforced at runtime.

### 4. Notification with Ordered Handlers — `UserCreatedNotification`

A single notification published via `mediator.PublishAsync(…)` is handled by two handlers decorated with `[NotificationHandlerOrder]`:

| Order | Handler | Action |
|-------|---------|--------|
| 1 | `SendWelcomeEmailHandler` | Logs sending welcome email |
| 2 | `CreateAuditLogHandler` | Logs creating audit log |

Handlers execute sequentially in order (the default `NotificationExecutionStrategy.Sequential`).

### 5. Open Generic Pipeline Behavior — `LoggingBehavior<TRequest, TResponse>`

Wraps every request with timing via `Stopwatch`:

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("   [Behavior] Handling {RequestName}", typeof(TRequest).Name);
        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();
        _logger.LogDebug("   [Behavior] Handled {RequestName} in {ElapsedMs}ms",
            typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);
        return response;
    }
}
```

Registered as an open generic: `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>))`.

## Conventions

- **Records everywhere** — Requests, responses, and notifications are all positional `record` types (immutable, value equality).
- **ValueTask returns** — All handlers return `ValueTask` or `ValueTask<T>` to avoid unnecessary allocations for synchronous completions.
- **Constructor-injected ILogger** — Handlers and behaviors take `ILogger<T>` via constructor DI. `GetUserQueryHandler` is the exception (no logger needed for a simple lookup).
- **Sealed is not enforced** — Unlike the core library, sample handler classes are not sealed. This is intentional to keep the demo simple.
- **Folder organization** — `Requests/` for request types + response DTOs, `Handlers/` for request handlers, `Notifications/` for notification types + handlers, `Behaviors/` for pipeline behaviors.
- **Namespace matches folder** — `MediatorLite.Sample.Requests`, `MediatorLite.Sample.Handlers`, etc.

## References

For detailed information, see:

- [references/project-structure.md](references/project-structure.md) — Complete file inventory, folder layout, naming conventions
- [references/registration-patterns.md](references/registration-patterns.md) — Full DI registration code, how to add new handlers/behaviors, reflection fallback details
