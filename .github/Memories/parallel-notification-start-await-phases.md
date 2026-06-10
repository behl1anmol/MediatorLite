# Memory: Parallel Notification Execution — Start/Await Two-Phase, Cooperative Concurrency, Error-Strategy Surfacing

## Metadata
- PatternId: parallel-notification-execution
- PatternVersion: 1
- Status: active
- Supersedes:
- CreatedAt: 2026-06-10
- LastValidatedAt: 2026-06-10
- ValidationEvidence: `dotnet test --filter NotificationTests` 17/17 incl. `PublishAsync_Parallel_StartPhase_InvokesEveryHandlerBeforeAwaitingAny` and `PublishAsync_Parallel_AwaitPhase_ObservesEveryStartedHandlerToCompletion`; full suite 78/78; generator emission read at `HandlerDiscoveryGenerator.GenerateParallelNotificationExecution` (~lines 1337-1392).

## Source Context
- Triggering task: A consumer reading the generated `Publish_*` for a `Parallel` notification asked whether dispatch is a bug because it "looks sequential". Outcome: clarify docs + rule + add phase-precise tests.
- Scope/system: Source-generator notification emission, `docs/notifications.md`, rule `40-notifications.md`.
- Date/time: 2026-06-10

## Memory
- **`Parallel` is cooperative `ValueTask` fan-out in two phases — not thread offload.**
  - **Start phase:** every handler's `HandleAsync` is *invoked* before any result is awaited. Each call runs the handler body synchronously up to its first suspending `await`, then returns a `ValueTask`. A synchronous throw is captured into a faulted `ValueTask` via a `try/catch` around the invocation, so one handler's sync throw can never stop later handlers from being started.
  - **Await phase:** the already-started `ValueTask`s are awaited in start order; faults are collected.
- **Concurrency is cooperative, bounded by handler yield points.** Handlers overlap only where they `await` an incomplete awaitable. Fully-synchronous handlers — or handlers that throw before any `await` (e.g. the `ParallelSyncThrowEvent` fixture) — run their bodies back-to-back during the start phase, **sequential in effect**, because neither yields the thread. This is correct and intentional: MediatorLite never wraps handlers in `Task.Run` (would cost a thread-pool hop + an allocation per handler). A handler with a real `await` genuinely overlaps the others.
- **Error strategy IS honored in `Parallel`** (this corrects a prior `docs/notifications.md` claim that `[NotificationError]` was "ignored" / that parallel "always aggregates"). Both strategies await every started handler — in-flight handlers cannot be cancelled — but the surfaced fault differs:
  - `ContinueAndAggregate` → throws `AggregateException` of all faults; a requested cancellation is rethrown as `OperationCanceledException` ahead of the aggregate.
  - `StopOnFirstError` (the library default) → rethrows the **first** fault in start order, **unwrapped**, via `ExceptionDispatchInfo.Capture(...).Throw()` (preserves original stack). "StopOnFirst" here means *which exception surfaces*, not stopping execution.
- `[NotificationHandlerOrder]` fixes **start order** (hence await order), not completion order.

## Why It Matters
- Kills two recurring misreads: (a) "parallel isn't actually concurrent" — it is, for handlers that yield; the sync-throw fixture is the misleading case; (b) "parallel ignores error strategy" — it does not. Both were doc-vs-implementation mismatches now reconciled.

## Applicability
- When to reuse: editing `GenerateParallelNotificationExecution`, answering/ documenting parallel notification semantics, or authoring parallel notification tests.
- Preconditions/limitations: genuine concurrency requires handlers with real async suspension points; purely synchronous handlers are sequential by nature. `StopOnFirstError` + `Parallel` still runs every handler to completion (cannot stop in-flight work).

## Actionable Guidance
- Keep these in sync: `docs/notifications.md` "### Parallel", rule `40-notifications.md` Rule 6 (both the `.claude/` and `.agents/` copies — they are separate, drift-prone duplicates), and `HandlerDiscoveryGenerator.GenerateParallelNotificationExecution`.
- Do **not** collapse the start and await phases into a single `await` loop — that serializes handlers that currently overlap.
- Pin behavior with `PublishAsync_Parallel_StartPhase_*` / `_AwaitPhase_*` and the gate-based `ParallelPhaseProbe` fixture in `tests/MediatorLite.Tests/SourceGeneration/`.
- Related files: `src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs`, `docs/notifications.md`, `.claude/rules/40-notifications.md`, `.agents/rules/40-notifications.md`, `tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs`, `tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs`.
- Related memory: `v2-typed-switch-dispatch-architecture` (PatternId `dispatch-architecture`) — the core typed-switch/`ValueTask` dispatch; this memory drills into the notification `Parallel` strategy it references.
