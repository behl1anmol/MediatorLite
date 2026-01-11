# Quick Start Guide

Get started with MediatorLite in minutes.

## Installation

```bash
dotnet add package MediatorLite
```

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

```csharp
// In Program.cs or Startup.cs
services.AddMediatorLite(options =>
{
    // Auto-register all handlers from assembly
    options.RegisterHandlersFromAssembly(typeof(Program).Assembly);
    
    // Optional: Add pipeline behaviors
    options.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
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
    public async ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken ct)
    {
        await _auditService.LogAsync($"User {notification.UserId} created");
    }
}

// Publish notification
await _mediator.PublishAsync(new UserCreatedNotification(user.Id, user.Email));
```

## Next Steps

- [Pipeline Behaviors](pipeline-behaviors.md) - Add logging, validation, and other cross-cutting concerns
- [Notifications](notifications.md) - Configure execution strategies and ordering
- [Migration from MediatR](migration-from-mediatr.md) - Migrate your existing MediatR code
