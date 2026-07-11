# Memory: Handler-Internal OperationCanceledException Is an Ordinary Fault Under ContinueAndAggregate

## Metadata
- PatternId: notification-oce-fault-semantics
- PatternVersion: 1
- Status: active
- Supersedes:
- CreatedAt: 2026-07-11
- LastValidatedAt: 2026-07-11
- ValidationEvidence: `PublishAsync_ParallelAggregate_HandlerInternalOce_AggregatesAllFaults` (OCE + InvalidOperationException both inside one AggregateException), `PublishAsync_SequentialAggregate_HandlerInternalOce_DoesNotSkipRemainingHandlers` (second handler still runs), `PublishAsync_ParallelAggregate_GenuineCancellation_SurfacesOceUnwrapped` (publish-token cancellation still unwrapped); full suite green.

## Source Context
- Triggering task: Repo-wide bug hunt. The parallel ContinueAndAggregate emission rethrew the first `OperationCanceledException` found in the fault list **unwrapped**, silently dropping every sibling fault; the sequential/StopOnFirst emissions had a blanket `catch (OperationCanceledException) { throw; }` that aborted the publish (skipping remaining handlers) even when the OCE came from a handler's own internal token, contradicting the documented "continue executing all handlers" contract.
- Scope/system: Source-generator notification emission (`GenerateSequentialNotificationExecution`, `GenerateParallelNotificationExecution`, `GenerateStopOnFirstNotificationExecution`), rule 40, `docs/notifications.md`, `ContinueAndAggregate` XML docs.
- Date/time: 2026-07-11

## Memory
- Key fact or decision: **An `OperationCanceledException` thrown by a handler is special only when the publish `CancellationToken` is actually cancelled.**
  - Sequential / StopOnFirst + ContinueAndAggregate emit `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` — genuine cancellation stops the loop; a handler-internal OCE is aggregated like any other fault and the remaining handlers still run.
  - Parallel + ContinueAndAggregate awaits every started handler, then: `ct.ThrowIfCancellationRequested()` (genuine cancellation surfaces unwrapped), otherwise `throw new AggregateException(exceptions)` with **all** faults, OCEs included as inner exceptions. Never rethrow an OCE unwrapped from the fault list — that silently drops sibling faults.
- Why it matters: The old behavior was an exception-loss bug: one flaky handler throwing OCE (an HttpClient timeout, a linked internal token) hid every other handler's genuine failure and, in sequential modes, prevented later handlers from running at all. Consumers alerting on `AggregateException` contents missed real faults.

## Applicability
- When to reuse: Editing any notification error-strategy emission; reviewing PRs that touch `catch (OperationCanceledException)` in generated or runtime dispatch code; answering "why did my publish stop early / where did my handler's exception go?".
- Preconditions/limitations: This is an observable behavior change from the previous emission (an OCE that used to surface unwrapped under ContinueAndAggregate now arrives inside `AggregateException` unless the publish token is cancelled). `StopOnFirstError` semantics are unchanged (first fault in order, unwrapped).

## Actionable Guidance
- Recommended future action: Keep the `when (ct.IsCancellationRequested)` filter pattern for any new strategy emission; add a mixed-fault fixture (OCE + ordinary exception) whenever a new error-handling path is introduced so exception loss cannot regress unnoticed.
- Related files/services/components: `src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs` (the three strategy emitters), `.claude/rules/40-notifications.md` + `.agents/rules/40-notifications.md` (cancellation invariant bullet), `docs/notifications.md`, `src/MediatorLite.Abstractions/Abstractions/Attributes.cs` (`ContinueAndAggregate` remarks), `tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs` (F8 region), `tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs`.
- Related memory: `parallel-notification-execution` (PatternId `parallel-notification-execution`) — the two-phase start/await contract this fault-surfacing rule plugs into.
