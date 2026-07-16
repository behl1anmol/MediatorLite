# Instruction: Add a New Notification Handler

## Intent

Publish an `INotification` to N handlers with compile-time choice of execution strategy (`Sequential` / `Parallel` / `StopOnFirst`) and error strategy (`StopOnFirstError` / `ContinueAndAggregate`). Strategy resolution is done by the source generator and inlined into the generated `Publish_*` method — there is no runtime configuration switch.

## When to use

- Emitting a domain event that one or more aggregates care about (e.g., `OrderPlaced`, `UserRegistered`).
- Implementing a fallback chain where the first successful handler wins (`StopOnFirst` + `ContinueAndAggregate`).
- Fanning out to independent side-effects (`Parallel`) such as emails, analytics, cache invalidation.

## Agent ownership

- **Primary:** `backend-developer`.
- **Review gate:** `code-reviewer` (strategy choice must match the described intent; parallel + shared state is a red flag).
- **Tester:** writes the three-strategy parity tests whenever a new strategy combination is introduced.

## Inputs / Preconditions

- You understand the async split: notification handlers return `ValueTask`; `IMediator.PublishAsync` returns `Task`.
- You know the resolution precedence enforced by the generator: **per-type `[NotificationExecution]` / `[NotificationError]` > assembly-level `[DefaultNotificationExecution]` / `[DefaultNotificationError]` > library defaults (`Sequential` / `StopOnFirstError`)**. See [Attributes.cs](src/MediatorLite.Abstractions/Abstractions/Attributes.cs) and [AGENTS.md](AGENTS.md).

## Numbered steps

1. **Define the notification** as a `record`. If the default `Sequential` + `StopOnFirstError` combination fits, you add no attributes at all:

   ```csharp
   public record OrderPlacedEvent(int OrderId, decimal Total) : INotification;
   ```

2. **Implement one or more handlers**. Each handler is a `sealed class` implementing `INotificationHandler<T>`. Ordering within the publish loop is controlled with `[NotificationHandlerOrder(N)]` (lower first; unspecified defaults to `0`).

   ```csharp
   [NotificationHandlerOrder(0)]
   public sealed class SendOrderConfirmationEmailHandler
       : INotificationHandler<OrderPlacedEvent>
   {
       public ValueTask HandleAsync(
           OrderPlacedEvent notification,
           CancellationToken cancellationToken = default)
       { /* ... */ return ValueTask.CompletedTask; }
   }

   [NotificationHandlerOrder(10)]
   public sealed class UpdateAnalyticsHandler
       : INotificationHandler<OrderPlacedEvent> { /* ... */ }
   ```

3. **Execution strategy — decision tree**:
   - **`Sequential` (default):** handlers run in `[NotificationHandlerOrder]` order, one after another. Use when handlers share state, order matters, or you want deterministic failure behaviour.
   - **`Parallel`:** all handlers start concurrently via `Task.WhenAll` inside the generated publisher. Use for independent, side-effect-only handlers (email, analytics, cache busts). Never use with shared mutable state.
   - **`StopOnFirst`:** handlers are attempted in order; execution stops as soon as one completes. Use for fallback/primary-secondary chains.

   Apply per-type:

   ```csharp
   [NotificationExecution(NotificationExecutionStrategy.Parallel)]
   public record ParallelEvent(string Message) : INotification;
   ```

   Or apply an assembly-wide default (in `AssemblyInfo.cs`):

   ```csharp
   [assembly: DefaultNotificationExecution(NotificationExecutionStrategy.Parallel)]
   ```

4. **Error strategy — decision tree**:
   - **`StopOnFirstError` (default):** first exception terminates the publish and is thrown to the caller. Use for fail-fast workflows.
   - **`ContinueAndAggregate`:** all handlers run; exceptions are collected into an `AggregateException`. Use when you want every side-effect attempted even if one fails.

   Combinations from the test fixtures:

   ```30:62:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
   public record UserCreatedEvent(int UserId, string Email) : INotification;

   [NotificationExecution(NotificationExecutionStrategy.Parallel)]
   [NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
   public record ParallelEvent(string Message) : INotification;

   [NotificationExecution(NotificationExecutionStrategy.StopOnFirst)]
   public record StopOnFirstEvent(string Message) : INotification;

   /// <summary>
   /// Notification configured for StopOnFirst execution with ContinueAndAggregate error strategy.
   /// This enables the "fallback pattern" where if one handler fails, the next is tried.
   /// </summary>
   [NotificationExecution(NotificationExecutionStrategy.StopOnFirst)]
   [NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
   public record StopOnFirstFallbackEvent(string Message) : INotification;

   /// <summary>
   /// Notification configured for StopOnFirst + StopOnFirstError (default error strategy).
   /// When the first handler fails, it should throw immediately without trying other handlers.
   /// </summary>
   [NotificationExecution(NotificationExecutionStrategy.StopOnFirst)]
   [NotificationError(NotificationErrorStrategy.StopOnFirstError)]
   public record StopOnFirstWithStopOnFirstErrorEvent(string Message) : INotification;

   /// <summary>
   /// Notification configured for StopOnFirst + ContinueAndAggregate where ALL handlers fail.
   /// Should throw AggregateException with all handler failures.
   /// </summary>
   [NotificationExecution(NotificationExecutionStrategy.StopOnFirst)]
   [NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
   public record AllFailStopOnFirstWithAggregateEvent(string Message) : INotification;
   ```

5. **Test parity** — when you add a **new strategy combination**, write three tests:
   - **Happy path:** every handler runs / the chosen subset runs.
   - **Failure path:** assert the documented behaviour (immediate throw vs. `AggregateException` vs. fallback success).
   - **Ordering path:** assert handlers fired in `[NotificationHandlerOrder]` order (for `Sequential` and `StopOnFirst`).

   Place them in [tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs](tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs).

6. **Verify the generator counted the handlers**. `MediatorLiteRegistration.NotificationHandlerCount` should increase by the number of new handlers:

   ```794:794:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
           sb.AppendLine($"        public static int NotificationHandlerCount => {notificationHandlers.Count};");
   ```

7. **Build & test**:

   ```powershell
   dotnet test MediatorLite.sln -c Release --filter FullyQualifiedName~Notification
   ```

   Expected exit code: `0`.

## Validation / Acceptance

- The notification has either zero strategy attributes (uses the library default) or an explicit `[NotificationExecution]` / `[NotificationError]` that matches the design intent.
- For new strategy combinations, three tests exist (happy / failure / ordering) under `tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs`.
- `NotificationHandlerCount` increased by exactly the number of new handlers.
- Parallel handlers do not share mutable state without synchronisation (verified by reading the handler class fields).

## Handoff / Exit criteria

- Hand back to the orchestrator with: the notification type, chosen execution/error strategy, handler count delta, and staged `diff_hash`.
- If the strategy combination is novel in this codebase, add a `.github/Memories/` note capturing the "when to use" reasoning.

## Related rules, skills, instructions

- Rules: [.cursor/rules/10-dispatch-invariants.mdc](.cursor/rules/10-dispatch-invariants.mdc), [.cursor/rules/20-source-generator.mdc](.cursor/rules/20-source-generator.mdc).
- Abstractions: [INotification.cs](src/MediatorLite.Abstractions/Abstractions/INotification.cs), [INotificationHandler.cs](src/MediatorLite.Abstractions/Abstractions/INotificationHandler.cs), [Attributes.cs](src/MediatorLite.Abstractions/Abstractions/Attributes.cs).
- Dispatcher: the generated `SourceGeneratedMediator` (emitted by [HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs)).
- Tests: [tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs](tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs), [tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs](tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs).
- Agent: [.cursor/agents/orchestrator.md](.cursor/agents/orchestrator.md).
- Related instructions: [add-new-request-handler.md](add-new-request-handler.md), [extend-source-generator.md](extend-source-generator.md).
