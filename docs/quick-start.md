# Quick Start Guide

Get started with MediatorLite v2 in minutes.

## Installation

```bash
dotnet add package MediatorLite
dotnet add package MediatorLite.SourceGeneration   # Required for O(1 dispatch
```

> ⚠️ **v2 Requirement:** Source generation is the **only** dispatch mechanism. Without `MediatorLite.SourceGeneration`, dispatch throws an `InvalidOperationException` with setup guidance on first use.

Optional (when a project only needs request/notification/validation contracts):

```bash
dotnet add package MediatorLite.Abstractions
```

## Package Selection and Versioning

Use this as a quick rule set for new users.

| Project Type | Install |
|--------------|---------|
| Application/API (v2) | `MediatorLite` + `MediatorLite.SourceGeneration` (both required) |
| Shared contracts library | `MediatorLite.Abstractions` |

How transitive install works:
- Installing `MediatorLite` also installs `MediatorLite.Abstractions`.
- Installing only `MediatorLite.SourceGeneration` does not install runtime contracts.

Compatibility matrix (safe combinations):

| Abstractions | MediatorLite | SourceGeneration | Supported |
|--------------|--------------|------------------|-----------|
| 2.x | 2.x | 2.x | Yes (recommended) |
| transitive | 2.x | 2.x | Yes |
| 2.x | 2.x | not installed | No (dispatch throws — no fallback) |
| 2.x | not installed | 2.x | No |
| 1.x | 2.x | 2.x | No |
| 2.x | 2.x | 1.x | No |

Versioning guideline:
- Keep all packages on the same major and minor version.
- Patch versions can differ, but lockstep is recommended for beginners.

## 1. Define a Request and Handler

```csharp
// Define a query request
public record GetUserQuery(int Id) : IRequest<User>;

// Define the response type
public record User(int Id, string Name, string Email);

// Implement the handler
public class GetUserQueryHandler : IRequestHandler<GetUserQuery, User>
{
    private readonly IUserRepository _repository;

    public GetUserQueryHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<User> HandleAsync(GetUserQuery request, CancellationToken cancellationToken = default)
    {
        return await _repository.GetByIdAsync(request.Id, cancellationToken);
    }
}
```

## 2. Register Services

### Source-Generated Registration (Required for v2)

**You must call `AddGeneratedHandlers()` before `AddMediatorLite()`** to enable O(1) dispatch:

```csharp
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // Registers handlers and the generated IMediator (typed dispatch)
    .AddMediatorLite();       // Optional diagnostic fallback; call order doesn't matter
```

`AddGeneratedHandlers()` registers:
- All `IRequestHandler<,>` implementations
- All `INotificationHandler<>` implementations
- All `IPipelineBehavior<,>` implementations (ordered by `[BehaviorOrder]`)
- The source-generated `IMediator` implementation (scoped; typed switch dispatch, no reflection, no boxing)

### Observability (on by default, compile-time opt-out)

Built-in logging and OpenTelemetry tracing are emitted inline by the source generator and are **on by default**. Opt out at compile time with assembly-level attributes:

```csharp
[assembly: DisableMediatorLogging]   // Generator emits no logging calls
[assembly: DisableMediatorTracing]   // Generator emits no ActivitySource calls
```

The log **level** is controlled through standard `Microsoft.Extensions.Logging` configuration. Generated code logs at `Debug` under the `MediatorLite.IMediator` category:

```csharp
services.AddLogging(b => b.AddFilter("MediatorLite.IMediator", LogLevel.Information));
```

For granular control, use the individual registration methods:

```csharp
services
    .AddGeneratedRequestHandlers()        // Only request handlers
    .AddGeneratedNotificationHandlers()   // Only notification handlers
    .AddGeneratedBehaviors()              // Only pipeline behaviors
    .AddMediatorLite();
```

### Manual DI Registration (Not Supported)

> ⚠️ **Not supported in v2:** There is no reflection fallback. Without `AddGeneratedHandlers()`, the `IMediator` registered by `AddMediatorLite()` throws an `InvalidOperationException` with setup guidance on first use — manual handler registrations alone cannot be dispatched.

```csharp
services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserQueryHandler>();
services.AddTransient<INotificationHandler<UserCreatedNotification>, SendWelcomeEmailHandler>();
services.AddMediatorLite();  // Without AddGeneratedHandlers(), dispatch throws at first use
```

### Excluding Types from Source Generation

Discovery is unconditional: every concrete handler in the compilation is registered. The
legacy `[MediatorGeneration(Skip = true)]` attribute is obsolete and has **no effect**. To
keep a handler out of registration, move it to an assembly the source generator does not
run on (for example, a test-support project).

## 3. Send Requests

```csharp
public class UserService
{
    private readonly IMediator _mediator;

    public UserService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<User> GetUserAsync(int id, CancellationToken ct)
    {
        return await _mediator.SendAsync(new GetUserQuery(id), ct);
    }
}
```

## 4. Commands (No Return Value)

```csharp
// Command without response
public record DeleteUserCommand(int Id) : IRequest;

// Handler for void commands
public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
    public async ValueTask HandleAsync(DeleteUserCommand request, CancellationToken cancellationToken = default)
    {
        await _repository.DeleteAsync(request.Id, cancellationToken);
    }
}
```

## 5. Notifications (Pub-Sub)

```csharp
// Define notification
public record UserCreatedNotification(int UserId, string Email) : INotification;

// Multiple handlers can subscribe
public class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification>
{
    public async ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken ct)
    {
        await _emailService.SendWelcomeAsync(notification.Email);
    }
}

public class CreateAuditLogHandler : INotificationHandler<UserCreatedNotification>
{
    public async ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken ct = default)
    {
        await _auditService.LogAsync($"User {notification.UserId} created");
    }
}

// Publish notification
await _mediator.PublishAsync(new UserCreatedNotification(user.Id, user.Email));
```

## 6. Notification Execution Strategies (v2)

In v2, notification strategies are controlled via **compile-time attributes**, not runtime options:

```csharp
// Apply strategy via compile-time attributes (v2 approach)
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record UserCreatedNotification(int UserId, string Email) : INotification;

// Or set an assembly-wide default once:
[assembly: DefaultNotificationExecution(NotificationExecutionStrategy.Parallel)]
[assembly: DefaultNotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
```

> ⚠️ **v2 Change:** `MediatorOptions` is gone, `AddMediatorLite` no longer accepts a configure lambda, and the old `[NotificationOptions]` attribute has been **removed**. Use `[NotificationExecution]` / `[NotificationError]` (or their `[assembly: Default...]` counterparts) instead.

| Strategy | Behavior | Error Strategy |
|----------|----------|----------------|
| `Sequential` | Execute handlers one-by-one in order | Applies |
| `Parallel` | Execute all handlers concurrently | Always aggregates* |
| `StopOnFirst` | Stop after first successful handler | Applies |

> *Parallel mode always aggregates exceptions because concurrent tasks cannot be stopped mid-execution.

See [Notifications](notifications.md) for detailed strategy documentation and error handling patterns.

## 7. Source Generator Diagnostics

The source generator exposes handler counts for diagnostics:

```csharp
using MediatorLite.Generated;

Console.WriteLine($"Request handlers: {MediatorLiteRegistration.RequestHandlerCount}");
Console.WriteLine($"Notification handlers: {MediatorLiteRegistration.NotificationHandlerCount}");
Console.WriteLine($"Behaviors: {MediatorLiteRegistration.BehaviorCount}");
```

## Next Steps

- [Pipeline Behaviors](pipeline-behaviors.md) - Add logging, validation, and other cross-cutting concerns
- [Notifications](notifications.md) - Configure execution strategies and ordering
- [Migration from MediatR](migration-from-mediatr.md) - Migrate your existing MediatR code
