# Notifications

Notifications implement a pub-sub pattern where multiple handlers can respond to a single notification.

## v2 Changes

In v2, notification execution and error strategies are **compile-time only**. They are baked into the generated `Publish_*` methods, so there is no runtime branching. Two orthogonal per-type attributes control strategy:

```csharp
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record UserCreatedNotification(int UserId, string Email) : INotification;
```

Assembly-level attributes provide defaults for every notification type in the assembly:

```csharp
[assembly: DefaultNotificationExecution(NotificationExecutionStrategy.Parallel)]
[assembly: DefaultNotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
```

Precedence (resolved per strategy, per notification, at compile time):

1. Per-notification attribute (`[NotificationExecution]` / `[NotificationError]`) — wins when present.
2. Assembly-level default (`[assembly: DefaultNotificationExecution]` / `[assembly: DefaultNotificationError]`).
3. Library defaults — `Sequential` for execution, `StopOnFirstError` for error.

> ⚠️ **Hard break in v2:** `MediatorOptions.NotificationExecutionStrategy`, `MediatorOptions.NotificationErrorStrategy`, `NotificationOptionsAttribute`, and `ISourceGeneratedMediator.GetNotificationOptions` have been **removed**. Use the attributes above.

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

## Execution Strategies (v2)

MediatorLite provides three execution strategies, selected via `[NotificationExecution]` (or the assembly-level default).

### Sequential (Default)

Handlers execute one after another in order. Best for handlers with dependencies or when order matters.

```csharp
[NotificationExecution(NotificationExecutionStrategy.Sequential)]
public record OrderCompletedNotification(int OrderId) : INotification;
```

**Error Strategy Behavior:**

| Error Strategy | Behavior |
|----------------|----------|
| `StopOnFirstError` | Stops execution immediately when a handler throws. Remaining handlers are **not** executed. |
| `ContinueAndAggregate` | Continues executing all handlers. All exceptions are collected and thrown as `AggregateException`. |

### Parallel

All handlers execute concurrently using `Task.WhenAll`. Best for independent handlers.

```csharp
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
public record UserCreatedNotification(int UserId) : INotification;
```

**Error Strategy Behavior:**

> ⚠️ **Important:** `[NotificationError]` is **ignored** for parallel execution.

Since all handlers start immediately and run concurrently, it's impossible to "stop on first error" - handlers cannot be cancelled mid-execution. Parallel mode **always aggregates exceptions**:

- All handlers run to completion
- If any fail, all exceptions are collected into `AggregateException`

### StopOnFirst

Executes handlers in order until one completes successfully ("first handler wins"). Useful for fallback patterns.

```csharp
[NotificationExecution(NotificationExecutionStrategy.StopOnFirst)]
public record CacheInvalidationNotification(string Key) : INotification;
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
[NotificationExecution(NotificationExecutionStrategy.StopOnFirst)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record GetDataNotification(string Key) : INotification;
```

## Strategy Comparison

| Strategy | Order Matters | Stops Early | Error Strategy |
|----------|--------------|-------------|----------------|
| Sequential | ✅ Yes | ❌ No | ✅ Applies |
| Parallel | ❌ No | ❌ No | ❌ Always aggregates |
| StopOnFirst | ✅ Yes | ✅ On success | ✅ Applies |

## Per-Notification Configuration (v2)

Apply one or both of the per-type attributes at compile time:

```csharp
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record HighPriorityNotification(string Message) : INotification;
```

Each attribute is independent: you can set only execution, only error, or both. Any strategy you do not set falls back — first to the assembly-level default (if declared), otherwise to the library default.

## Assembly-Level Defaults (v2)

Declare defaults once per assembly instead of repeating the per-type attributes on every notification:

```csharp
// AssemblyInfo.cs (or any file in the assembly)
[assembly: DefaultNotificationExecution(NotificationExecutionStrategy.Parallel)]
[assembly: DefaultNotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
```

Both attributes are optional and independent. Per-type `[NotificationExecution]` / `[NotificationError]` always win when present.

## Removed APIs (v2)

The following runtime APIs have been **removed** — they are replaced by the compile-time attributes above:

- `MediatorOptions.NotificationExecutionStrategy`
- `MediatorOptions.NotificationErrorStrategy`
- `NotificationOptionsAttribute` (replaced by split `[NotificationExecution]` + `[NotificationError]`)
- `ISourceGeneratedMediator.GetNotificationOptions(Type)` (strategies are now inlined into `Publish_*`)

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
