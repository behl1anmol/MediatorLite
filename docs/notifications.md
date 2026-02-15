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
    public async ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken ct = default)
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
```

## Publishing Notifications

```csharp
await mediator.PublishAsync(new UserCreatedNotification(user.Id, user.Email));
```

## Execution Strategies

MediatorLite provides three execution strategies, each with specific error handling behavior.

### Sequential (Default)

Handlers execute one after another in order. Best for handlers with dependencies or when order matters.

```csharp
services.AddMediatorLite(options =>
{
    options.NotificationExecutionStrategy = NotificationExecutionStrategy.Sequential;
});
```

**Error Strategy Behavior:**

| Error Strategy | Behavior |
|----------------|----------|
| `StopOnFirstError` | Stops execution immediately when a handler throws. Remaining handlers are **not** executed. |
| `ContinueAndAggregate` | Continues executing all handlers. All exceptions are collected and thrown as `AggregateException`. |

### Parallel

All handlers execute concurrently using `Task.WhenAll`. Best for independent handlers.

```csharp
services.AddMediatorLite(options =>
{
    options.NotificationExecutionStrategy = NotificationExecutionStrategy.Parallel;
});
```

**Error Strategy Behavior:**

> ⚠️ **Important:** `NotificationErrorStrategy` is **ignored** for parallel execution.

Since all handlers start immediately and run concurrently, it's impossible to "stop on first error" - handlers cannot be cancelled mid-execution. Parallel mode **always aggregates exceptions**:

- All handlers run to completion
- If any fail, all exceptions are collected into `AggregateException`

### StopOnFirst

Executes handlers in order until one completes successfully ("first handler wins"). Useful for fallback patterns.

```csharp
services.AddMediatorLite(options =>
{
    options.NotificationExecutionStrategy = NotificationExecutionStrategy.StopOnFirst;
});
```

**Error Strategy Behavior:**

| Error Strategy | Behavior |
|----------------|----------|
| `StopOnFirstError` | If a handler throws, stops immediately and propagates the exception. No fallback. |
| `ContinueAndAggregate` | If a handler throws, tries the next handler. Stops on first **success**. If all handlers fail, throws `AggregateException`. |

**Fallback Pattern Example:**

```csharp
[NotificationHandlerOrder(1)]
public class PrimaryCacheHandler : INotificationHandler<GetDataNotification> { }

[NotificationHandlerOrder(2)]
public class FallbackDatabaseHandler : INotificationHandler<GetDataNotification> { }

// Configure to try next handler on failure
[NotificationOptions(
    ExecutionStrategy = NotificationExecutionStrategy.StopOnFirst,
    ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate)]
public record GetDataNotification(string Key) : INotification;
```

## Strategy Comparison

| Strategy | Order Matters | Stops Early | Error Strategy |
|----------|--------------|-------------|----------------|
| Sequential | ✅ Yes | ❌ No | ✅ Applies |
| Parallel | ❌ No | ❌ No | ❌ Always aggregates |
| StopOnFirst | ✅ Yes | ✅ On success | ✅ Applies |

## Per-Notification Configuration

Override global settings using `[NotificationOptions]`:

```csharp
[NotificationOptions(
    ExecutionStrategy = NotificationExecutionStrategy.Parallel,
    ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate,
    OverrideGlobal = true)]
public record HighPriorityNotification(string Message) : INotification;
```

## Global Configuration

```csharp
services.AddMediatorLite(options =>
{
    options.NotificationExecutionStrategy = NotificationExecutionStrategy.Sequential;
    options.NotificationErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate;
});
```

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

## Best Practices

1. **Use Sequential** when handlers have dependencies or must run in order
2. **Use Parallel** for independent handlers (emails, logging, analytics)
3. **Use StopOnFirst** for fallback/circuit-breaker patterns
4. **Use `ContinueAndAggregate`** in production to ensure resilience
5. **Handle exceptions** in handlers to prevent cascade failures
