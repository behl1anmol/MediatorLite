# Lesson: ServiceCollection Behavior Registration and Validation Boundaries

## Metadata
- PatternId: servicecollectionextensions-behavior-registration
- PatternVersion: 1
- Status: deprecated
- Supersedes:
- DeprecatedAt: 2026-06-10
- DeprecationReason: This lesson targets the v1 reflection/options architecture that
  the v2 typed-switch rewrite removed. The referenced surface no longer exists:
  `MediatorOptions.cs`, `MediatorOptions.AddBehavior<TBehavior>()`,
  `AddMediatorBehavior`, the reflection-fallback behavior invocation in
  `Internal/Mediator.cs`, and the entire `tests/MediatorLite.Tests/Reflection/`
  suite are deleted. Behavior discovery/expansion and registration are now owned by
  the source generator (closed + open-generic behaviors expanded at compile time;
  `ValidationBehavior` emitted first). `PipelineBehaviorTypeResolver.cs` still exists
  but is orphaned off the dispatch path. Kept for history; do not reuse as guidance.
  See `.github/Memories/v2-typed-switch-dispatch-architecture.md`.

## Task Context
- Triggering task: Review findings for ServiceCollectionExtensions and related options validation.
- Date/time: 2026-03-19
- Impacted area: src/MediatorLite/Configuration/ServiceCollectionExtensions.cs, src/MediatorLite/Configuration/MediatorOptions.cs, tests/MediatorLite.Tests/Reflection/

## Mistake
- What went wrong: Closed behavior registration uses FirstOrDefault over implemented IPipelineBehavior<,> interfaces.
- Expected behavior: Either register all implemented pipeline interfaces or fail fast with an explicit error when a type maps to multiple interfaces.
- Actual behavior: Only the first discovered interface is registered; additional interfaces can be silently dropped.
- Related correctness issue: AddMediatorLite throws ArgumentException with paramName configure when behaviorType is invalid, which misdirects debugging.
- Related validation issue: MediatorOptions.AddBehavior<TBehavior>() accepts any class and defers interface validation until AddMediatorLite.
- Convention issue: AddMediatorLite and AddMediatorBehavior expose different exception contracts (paramName and message shape), creating inconsistent failure handling.

## Root Cause Analysis
- Primary cause: Behavior type validation and interface mapping happen late and differently across registration entry points.
- Contributing factors:
  - Shared selection pattern projects to a single interface using FirstOrDefault instead of policy-driven mapping.
  - AddBehavior<TBehavior>() does not perform immediate interface validation.
  - Existing tests cover successful registration but not descriptor shape, multi-interface mapping, or exception metadata.
- Detection gap: No test currently asserts the semantics for a closed behavior implementing multiple IPipelineBehavior<,> interfaces.

## Resolution
- Fix implemented:
  - Added a shared resolver in src/MediatorLite/Configuration/PipelineBehaviorTypeResolver.cs.
  - Policy chosen: register closed behaviors for every implemented closed IPipelineBehavior<,> interface.
  - Updated AddMediatorLite and AddMediatorBehavior to use shared service-type resolution.
  - Updated MediatorOptions.AddBehavior<TBehavior>() to validate immediately (fail fast).
  - Normalized invalid-behavior ArgumentException paramName to behaviorType.
  - Updated reflection behavior invocation to select the matching interface for request/response (not first interface).
- Why this fix works:
  - Registration and runtime invocation now use centralized interface resolution rules.
  - Multi-interface behaviors no longer lose registrations and resolve correctly per request type.
  - Invalid behavior types are rejected at options composition time and DI registration boundaries with consistent diagnostics.
- Verification performed:
  - Targeted tests: 25 passed, 0 failed.
  - Full tests: 129 passed, 0 failed.
  - Targeted benchmark class run: PipelineBenchmarks completed and preserved allocation profile expectations.

## Verification and Analysis Gaps
- Remaining gap:
  - No dedicated benchmark isolates startup registration overhead of additional interface resolution.
  - Runtime path impact is expected to be negligible for source-generated dispatch because changes are in startup registration and reflection fallback.

## Preventive Actions
- Added tests in tests/MediatorLite.Tests/Reflection/ServiceCollectionExtensionsTests.cs for:
  - Multi-interface closed behavior registration via AddMediatorLite.
  - Multi-interface closed behavior registration via AddMediatorBehavior.
  - Exception contract parity for invalid behavior types.
- Added tests in tests/MediatorLite.Tests/Reflection/MediatorOptionsTests.cs for:
  - AddBehavior<TBehavior>() invalid type rejection timing and paramName contract.
- Added test in tests/MediatorLite.Tests/Reflection/PipelineBehaviorTests.cs for:
  - Runtime invocation correctness for closed behaviors implementing multiple interfaces.
- Follow-up candidate:
  - Add optional startup-focused microbenchmark if DI registration throughput becomes a concern.

## Reuse Guidance
- Apply this lesson when adding or modifying DI registration APIs and option-builder methods.
- Require explicit policy and tests for one-to-many interface mappings before release.