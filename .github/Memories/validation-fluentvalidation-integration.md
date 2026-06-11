# Memory: FluentValidation replaces the in-house validation model

## Metadata
- PatternId:            ADR-0008
- PatternVersion:       1
- Status:               active
- Supersedes:           (none)
- CreatedAt:            2026-06-11
- LastValidatedAt:      2026-06-11
- ValidationEvidence:   branch claude/fluentvalidation-integration-x0hj0d; tests/MediatorLite.Tests (80 passing) incl. SourceGeneration.ValidationTests + UnitTests.FluentValidationBehaviorTests

## Source Context
- Triggering task:      Integrate FluentValidation for first-class validation support.
- Scope/system:         src/MediatorLite.FluentValidation/**, src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs, src/MediatorLite.Abstractions/Validation/**, src/MediatorLite/Validation/** (removed)
- Date/time:            2026-06-11

## Memory
- Key fact or decision: Validation is now powered by FluentValidation. The in-house
  `IValidator<T>`, `DataAnnotationsValidator<T>`, `ValidationResult`, and core `ValidationBehavior`
  were removed. A new opt-in `MediatorLite.FluentValidation` package provides
  `FluentValidationBehavior<TRequest,TResponse>`, which the source generator wires as the
  outermost pipeline behavior for validated request types. FluentValidation failures are mapped
  to the retained `MediatorLite.Validation.ValidationException` / `ValidationError`.
- Why it matters:       Keeps core `MediatorLite` dependency-free ("lite"), gives consumers the
  de-facto .NET validation library, and preserves the performance story: validator registration
  is source-generated (no `AddValidatorsFromAssembly` reflection scan) and dispatch stays the
  unrolled compile-time pipeline. Same pipeline-behavior shape as MediatR, so migration is
  familiar and the benchmark comparison stays honest.

## Applicability
- When to reuse:        Any decision about where validation runs or how third-party validators
  are discovered. Default: discover validators in the generator, run them in a compiled behavior
  in a satellite package; never re-emit a third party's API as generator string templates.
- Preconditions/limitations: The generator (netstandard2.0) cannot reference FluentValidation; it
  matches `FluentValidation.IValidator<T>` by namespace+name only and emits a type reference to
  the compiled behavior.

## Actionable Guidance
- Recommended future action: Author validators as `AbstractValidator<T>`; rely on
  `AddGeneratedHandlers()` for wiring. Keep the error contract in `MediatorLite.Abstractions`.
- Related files/services/components: `FluentValidationBehavior.cs`, `HandlerDiscoveryGenerator.cs`
  (`GetValidatorInfo`, `DetermineValidationTargets`, `AddGeneratedValidators`, `MEDL1001`),
  `docs/validation.md`, `.claude/rules/50-validation.md`.

## Context

The in-house validator was intentionally basic (no rule chains, async composition, or rich
metadata). The user asked for first-class FluentValidation while preserving "blazing fast" and
the "beats MediatR" claim. The generator already discovered `IValidator<T>` and emitted a
`ValidationBehavior` arm, so the seam was the validator-discovery match string and the emitted
behavior type name.

## Options considered

- **Option A — Keep validation as a pipeline behavior (chosen).** Emit a compiled
  `FluentValidationBehavior` outermost. Pros: FV-API coupling stays in compiled/testable code,
  generator stays uniform, idiomatic, already beats MediatR. Cons: one DI resolve + state machine
  per validated request (negligible vs FV's own cost).
- **Option B — Inline FV calls into generated `Send_X`.** Rejected: brittle string codegen against
  an API the netstandard2.0 generator cannot type-check.
- **Option C — Inline guard + compiled helper.** Deferred as a future micro-optimization; adds a
  bespoke validation branch to the generator for marginal per-request gain.

Packaging: a new `MediatorLite.FluentValidation` package (chosen) vs adding FV to core (rejected —
breaks the "lite" positioning by forcing the dependency on every consumer).

Error contract: map to MediatorLite's `ValidationException` (chosen, uniform catch surface) vs
throw FluentValidation's native exception (rejected).

## Decision

We use Option A in a new opt-in `MediatorLite.FluentValidation` package, mapping FV failures to
`MediatorLite.Validation.ValidationException`. The generator discovers FV validators, registers
only those whose request type has a handler in the same compilation, emits
`FluentValidationBehavior` outermost, and reports `MEDL1001` if validators exist but the package
is not referenced.

## Consequences

- Breaking change: removed public in-house validation types. Migration documented in
  `docs/validation.md` (and applies to the v1→v2 line). `ValidationException`/`ValidationError`
  are unchanged, so existing catch blocks keep working.
- Fixed a latent ordering bug: validation is now genuinely outermost (it previously sat inside
  open-generic behaviors), matching the documented invariant and pinned by
  `ValidationBehavior_RunsOutermost_ShortCircuitsOtherBehaviors`.
