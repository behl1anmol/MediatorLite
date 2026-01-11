# Notifications

Notifications implement a pub-sub pattern where multiple handlers can respond to a single notification.

## Defining Notifications

```csharp
public record UserCreatedNotification(int UserId, string Email) : INotification;
```

## Creating Handlers

```csharp
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
```

## Publishing Notifications

```csharp
await mediator.PublishAsync(new UserCreatedNotification(user.Id, user.Email));
```

## Execution Strategies

Configure how handlers execute:

### Global Configuration

```csharp
services.AddMediatorLite(options =>
{
    options.NotificationExecutionStrategy = NotificationExecutionStrategy.Sequential;
    options.NotificationErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate;
});
```

### Per-Notification Configuration

```csharp
[NotificationOptions(
    ExecutionStrategy = NotificationExecutionStrategy.Parallel,
    ErrorStrategy = NotificationErrorStrategy.StopOnFirstError)]
public record CriticalNotification(string Message) : INotification;
```

### Available Strategies

| Strategy | Description |
|----------|-------------|
| `Sequential` | Execute handlers one after another (default) |
| `Parallel` | Execute all handlers concurrently |
| `StopOnFirst` | Stop after first handler completes |

### Error Strategies

| Strategy | Description |
|----------|-------------|
| `StopOnFirstError` | Stop on first exception |
| `ContinueAndAggregate` | Continue, throw AggregateException at end |

## Handler Ordering

Control execution order with `[NotificationHandlerOrder]`:

```csharp
[NotificationHandlerOrder(1)] // Executes first
public class PrimaryHandler : INotificationHandler<UserCreatedNotification> { }

[NotificationHandlerOrder(2)] // Executes second
public class SecondaryHandler : INotificationHandler<UserCreatedNotification> { }
```

Handlers without the attribute default to order `0`.

## Error Handling

```csharp
try
{
    await mediator.PublishAsync(notification);
}
catch (AggregateException ex)
{
    // Multiple handlers threw exceptions
    foreach (var inner in ex.InnerExceptions)
    {
        _logger.LogError(inner, "Handler failed");
    }
}
```
