# Notification Strategy Rules

Notifications are fan-out — `PublishAsync` dispatches one `INotification` to
every matching `INotificationHandler<T>`. Strategy resolution happens **at
compile time only**, inside the source generator. There is no runtime options
API.

## Rule 1 — Resolution precedence

The generator picks a strategy per notification type using this precedence,
from highest to lowest:

1. Per-notification `[NotificationExecution]` / `[NotificationError]`
2. Assembly `[assembly: DefaultNotificationExecution]` /
   `[assembly: DefaultNotificationError]`
3. Library defaults: `Sequential` (execution) / `StopOnFirstError` (error)

The result is **inlined** into the generated `Publish_*` method as a single
branch-free path. Do not try to change strategies at runtime; there is
nowhere to plug in.

The attributes are compile-time only and documented as such:

```109:116:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotificationExecutionAttribute(NotificationExecutionStrategy strategy) : Attribute
{
    /// <summary>
    /// Gets the execution strategy for this notification type.
    /// </summary>
    public NotificationExecutionStrategy Strategy { get; } = strategy;
}
```

## Rule 2 — `NotificationOptionsAttribute` is deleted

The old `NotificationOptionsAttribute` and any runtime `NotificationOptions` /
`PublishOptions` class have been removed. Do not reintroduce them. If you
find one in a PR, delete it and reach for the compile-time attributes
instead.

## Rule 3 — Strategy enum values

```6:48:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
public enum NotificationExecutionStrategy
{
    Sequential = 0,
    Parallel = 1,
    StopOnFirst = 2
}
```

- `Sequential` — one handler at a time, in order. Default.
- `Parallel` — `ValueTask` fan-out: every handler is started before any is
  awaited (no task array, no `Task.WhenAll`). See Rule 6.
- `StopOnFirst` — call handlers in order; stop on the first successful
  completion. Pair with `ContinueAndAggregate` for "fallback" semantics.

## Rule 4 — Ordering via `[NotificationHandlerOrder]`

Handlers are pre-sorted at compile time. Lower values run first; absent
attribute implies order 0. This applies in `Sequential` and `StopOnFirst`
execution modes; in `Parallel` it controls the order handlers are *started*
(and therefore the order they are awaited) — see Rule 6.

```140:180:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
public class UserCreatedEventHandler1 : INotificationHandler<UserCreatedEvent>
{
    ...
}

[NotificationHandlerOrder(1)]
public class UserCreatedEventHandler2 : INotificationHandler<UserCreatedEvent>
{
    ...
}

[NotificationHandlerOrder(2)]
public class UserCreatedEventHandler3 : INotificationHandler<UserCreatedEvent>
{
    ...
}
```

## Rule 5 — Declaration pattern

Declare the strategy on the notification record alongside the ordering on
handlers. Both attributes travel with the type:

```33:38:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record ParallelEvent(string Message) : INotification;

[NotificationExecution(NotificationExecutionStrategy.StopOnFirst)]
public record StopOnFirstEvent(string Message) : INotification;
```

Any runtime-only strategy override in a new PR is wrong — fail review and
point the author at this rule.

## Rule 6 — Parallel execution is two phases: start, then await

`Parallel` is **cooperative `ValueTask` fan-out**, not thread offload. The
generated `Publish_*` method splits into two phases; reading it without the
split in mind is what makes it look "sequential".

**Start phase** — every handler's `HandleAsync` is *invoked* before any result
is awaited. Each call runs the handler body synchronously up to its first
suspending `await`, then hands back a `ValueTask` for the remainder. A handler
that throws synchronously is captured into a faulted `ValueTask` (a `try/catch`
around the invocation), so a sync throw can never stop later handlers from being
started:

```csharp
ValueTask vt1;
try { vt1 = h1.HandleAsync(notification, ct); }
catch (Exception ex) { vt1 = ValueTask.FromException(ex); }
ValueTask vt2;
try { vt2 = h2.HandleAsync(notification, ct); }
catch (Exception ex) { vt2 = ValueTask.FromException(ex); }
```

**Await phase** — the already-started `ValueTask`s are awaited in start order,
collecting faults per the error strategy.

Invariants:

- **Concurrency is cooperative, not parallel threads.** Handlers overlap only at
  their `await` suspension points. Handlers whose bodies are fully synchronous —
  or that throw before any `await` — run back-to-back during the start phase,
  *sequentially in effect*, because neither yields the thread. This is correct
  and intentional: never wrap handlers in `Task.Run` (a thread-pool hop and an
  allocation per handler). A handler with a real `await` genuinely overlaps.
- **Every started handler is awaited**, regardless of error strategy — in-flight
  handlers cannot be stopped. The strategy only decides which fault surfaces:
  `ContinueAndAggregate` throws an `AggregateException` of all faults;
  `StopOnFirstError` rethrows the first fault (in start order), unwrapped.
- **Cancellation is special only when it is genuine.** A handler's own
  `OperationCanceledException` (its internal timeout/linked token — the publish
  token is not cancelled) is an ordinary fault: under `ContinueAndAggregate` it
  is aggregated with its siblings' faults (never rethrown unwrapped — that would
  silently drop the sibling faults), and in `Sequential`/`StopOnFirst` modes the
  remaining handlers still run. Only when the **publish** `CancellationToken` is
  actually cancelled does an unwrapped `OperationCanceledException` surface (and
  sequential execution stop). Pinned by the
  `PublishAsync_*Aggregate_HandlerInternalOce_*` and
  `PublishAsync_ParallelAggregate_GenuineCancellation_*` tests.
- `[NotificationHandlerOrder]` fixes **start order** (hence await order), not
  completion order.

The two phases are pinned by `PublishAsync_Parallel_StartPhase_*` and
`PublishAsync_Parallel_AwaitPhase_*` in
`tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs`. Do not collapse
the two phases into a single `await` loop — that serializes handlers that
currently overlap.
