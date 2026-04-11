# Quick Start Guide

Get started with MediatorLite v2 in minutes.

## Installation

```bash
dotnet add package MediatorLite
dotnet add package MediatorLite.SourceGeneration   # Required for O(1 dispatch
```

> ⚠️ **v2 Requirement:** Source generation is now the primary dispatch mechanism. Without `MediatorLite.SourceGeneration`, the library falls back to deprecated reflection-based dispatch.

Optional (when a project only needs request/notification/validation contracts):

```bash
dotnet add package MediatorLite.Abstractions
```

## Package Selection and Versioning

Use this as a quick rule set for new users.

| Project Type | Install |
|--------------|---------|
| Application/API (v2 recommended) | `MediatorLite` + `MediatorLite.SourceGeneration` |
| Application/API (deprecated) | `MediatorLite` only (reflection fallback) |
| Shared contracts library | `MediatorLite.Abstractions` |

How transitive install works:
- Installing `MediatorLite` also installs `MediatorLite.Abstractions`.
- Installing only `MediatorLite.SourceGeneration` does not install runtime contracts.

Compatibility matrix (safe combinations):

| Abstractions | MediatorLite | SourceGeneration | Supported |
|--------------|--------------|------------------|-----------|
| 2.x | 2.x | 2.x | Yes (recommended) |
| transitive | 2.x | 2.x | Yes |
| 2.x | 2.x | not installed | Yes (deprecated reflection fallback) |
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
    .AddGeneratedHandlers()   // MUST be called first — registers handlers and O(1) dispatch
    .AddMediatorLite(options =>
    {
        options.EnableBuiltInLogging = true;   // Observability options still work
        options.EnableTracing = true;
    });
```

`AddGeneratedHandlers()` registers:
- All `IRequestHandler<,>` implementations
- All `INotificationHandler<>` implementations
- All `IPipelineBehavior<,>` implementations (ordered by `[BehaviorOrder]`)
- The `ISourceGeneratedMediator` for O(1) dispatch

For granular control, use the individual registration methods:

```csharp
services
    .AddGeneratedRequestHandlers()        // Only request handlers
    .AddGeneratedNotificationHandlers()   // Only notification handlers
    .AddGeneratedBehaviors()              // Only pipeline behaviors
    .AddMediatorLite();
```

### Manual DI Registration (Deprecated)

> ⚠️ **Deprecated in v2:** Manual DI registration uses the reflection fallback which is deprecated. Use source generation for O(1) dispatch.

```csharp
services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserQueryHandler>();
services.AddTransient<INotificationHandler<UserCreatedNotification>, SendWelcomeEmailHandler>();
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddMediatorLite();  // Falls back to reflection when ISourceGeneratedMediator is not registered
```

### Excluding Types from Source Generation

Use `[MediatorGeneration(Skip = true)]` to prevent a handler from being discovered by the source generator:

```csharp
[MediatorGeneration(Skip = true)]
public class TestOnlyHandler : IRequestHandler<TestQuery, string>
{
    // This handler will NOT be registered by AddGeneratedHandlers()
    // Register it manually if needed
}
```

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
// Apply strategy via attribute (v2 approach)
[NotificationOptions(
    ExecutionStrategy = NotificationExecutionStrategy.Parallel,
    ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate)]
public record UserCreatedNotification(int UserId, string Email) : INotification;
```

> ⚠️ **v2 Change:** Runtime `MediatorOptions.NotificationExecutionStrategy` and `NotificationErrorStrategy` are ignored in favor of the `[NotificationOptions]` attribute.

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
