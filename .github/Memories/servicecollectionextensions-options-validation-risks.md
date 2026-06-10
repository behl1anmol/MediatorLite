# Memory: Behavior Registration Policy and Validation Timing in MediatorLite DI

## Metadata
- PatternId: servicecollectionextensions-options-validation-risks
- PatternVersion: 1
- Status: deprecated
- SupersededBy: dispatch-architecture (PatternVersion 2 — `.github/Memories/v2-typed-switch-dispatch-architecture.md`)
- DeprecatedAt: 2026-06-10
- DeprecationReason: The v2 typed-switch dispatch rewrite deleted the entire surface
  this memory describes — `MediatorOptions.cs`, `AddMediatorBehavior`, the
  `IMediator` runtime `Mediator.cs` and its reflection-fallback invocation matching,
  and the `tests/MediatorLite.Tests/Reflection/` suite are all gone.
  `PipelineBehaviorTypeResolver.cs` still physically exists but is orphaned (no source
  references it on the dispatch path). Behavior expansion/registration is now owned by
  the source generator. Do not reuse this guidance; see the superseding memory.

## Source Context
- Triggering task: Capture latest review findings for ServiceCollectionExtensions and options validation.
- Scope/system: Core DI registration and options builder paths.
- Date/time: 2026-03-19

## Memory
- Registration policy: closed behaviors are mapped to every implemented closed IPipelineBehavior<,> interface.
- Shared resolver contract lives in src/MediatorLite/Configuration/PipelineBehaviorTypeResolver.cs and is used by:
  - src/MediatorLite/Configuration/MediatorOptions.cs
  - src/MediatorLite/Configuration/ServiceCollectionExtensions.cs
  - src/MediatorLite/Internal/Mediator.cs (reflection fallback invocation matching)
- MediatorOptions.AddBehavior<TBehavior>() now validates immediately and rejects invalid behavior types at option composition time.
- AddMediatorLite and AddMediatorBehavior now use consistent invalid-type ArgumentException metadata (paramName: behaviorType).

## Why It Matters
- Prevents silent behavior loss for multi-interface implementations.
- Keeps diagnostics predictable across configuration and DI registration entry points.
- Reduces late-failure risk by validating behavior types earlier.

## Applicability
- Reuse when modifying:
  - src/MediatorLite/Configuration/ServiceCollectionExtensions.cs
  - src/MediatorLite/Configuration/MediatorOptions.cs
  - tests/MediatorLite.Tests/Reflection/ServiceCollectionExtensionsTests.cs
  - tests/MediatorLite.Tests/Reflection/MediatorOptionsTests.cs
- Preconditions/limitations:
- Applies to runtime behavior registration and reflection fallback behavior invocation.
- Source-generated happy path performance should remain effectively unchanged; startup-only registration checks add small fixed overhead.

## Actionable Guidance
- Reuse PipelineBehaviorTypeResolver for any future behavior-type validation or interface mapping changes.
- If introducing new behavior registration APIs, align exception contract with paramName behaviorType and existing message shape.
- Keep tests covering:
  - Multi-interface registration mapping
  - Early AddBehavior validation
  - Contract parity between AddMediatorLite and AddMediatorBehavior