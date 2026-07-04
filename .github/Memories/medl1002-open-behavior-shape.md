# Memory: Open Generic Behavior Expansion Contract — Canonical Shape or MEDL1002

## Metadata
- PatternId: open-behavior-expansion-contract
- PatternVersion: 1
- Status: active
- Supersedes:
- CreatedAt: 2026-07-02
- LastValidatedAt: 2026-07-02
- ValidationEvidence: `SourceGeneratorDriverTests.ConstrainedOpenBehavior_ReportsMedl1002_AndIsNotRegistered` (extra-constraint behavior → single MEDL1002, type absent from generated output) and `CanonicalOpenBehavior_DoesNotReportMedl1002` (canonical shape still expands); full suite 87/87.

## Source Context
- Triggering task: Repo-wide bug hunt found `ExpandBehaviors` blindly substituting every discovered (request, response) pair into any open behavior — a partially-open behavior (`Foo<TResponse> : IPipelineBehavior<ConcreteReq, TResponse>`) or one with extra constraints (`where TRequest : IRequest<TResponse>, IAuditable`) produced `.g.cs` code that failed to compile with opaque errors.
- Scope/system: Source generator behavior discovery/expansion (`HandlerDiscoveryGenerator`), generated DI registration, and the unrolled request pipelines.
- Date/time: 2026-07-02

## Memory
- Key fact or decision: **Open generic pipeline behaviors are expandable only in the canonical shape** — exactly two type parameters, used directly and in order as `IPipelineBehavior<TRequest, TResponse>`'s type arguments, with no constraints beyond the interface-mandated `where TRequest : IRequest<TResponse>` (no extra constraint types, no `class`/`struct`/`notnull`/`unmanaged`/`new()`). The check is `IsSupportedOpenShape` in `HandlerDiscoveryGenerator.cs`, evaluated at discovery time in `GetBehaviorInfo`. Anything else sets `BehaviorInfo.HasUnsupportedOpenShape`, is **excluded from expansion and registration**, and is surfaced in `Execute` as the **MEDL1002 warning** (registered in `AnalyzerReleases.Unshipped.md`), naming the offending behavior.
- Why it matters: Expansion works by textually substituting each discovered (request, response) pair for the class's type parameters. Outside the canonical shape that substitution produces closed types with wrong arity, swapped parameters, or unsatisfied constraints — generated code that fails the *consumer's* build with confusing errors pointing into a `.g.cs` file. Skipping + warning turns a cryptic build break into one actionable diagnostic; closed behaviors (bound to a concrete request type) remain unrestricted and are the escape hatch.

## Applicability
- When to reuse: Reviewing or extending `GetBehaviorInfo`/`IsSupportedOpenShape`/`ExpandBehaviors`; answering "why isn't my open behavior running?" (check build output for MEDL1002); designing any future generator feature that closes user generics over discovered types (e.g. open generic validators would need the same shape gate).
- Preconditions/limitations: The gate is shape-based, not semantic — it does not attempt per-request constraint satisfaction (a behavior constrained to `IAuditable` is skipped entirely rather than applied to only the auditable requests). Lifting that limitation means filtering the request/response pairs per constraint instead of rejecting the behavior; until then MEDL1002 is the contract.

## Actionable Guidance
- Recommended future action: Keep MEDL1002 warning-severity (consumer builds with warnings-as-errors will still fail fast, others keep building); never let an unsupported shape silently vanish without the diagnostic, and never emit the closed type "optimistically". If a consumer needs a constrained behavior, point them to a closed behavior per request type.
- Related files/services/components: `src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs` (`IsSupportedOpenShape`, `GetBehaviorInfo`, `ExpandBehaviors`, `UnsupportedOpenBehaviorShape` descriptor), `src/MediatorLite.SourceGeneration/AnalyzerReleases.Unshipped.md`, `tests/MediatorLite.Tests/UnitTests/SourceGeneratorDriverTests.cs`, rule `.claude/rules/30-pipeline-behaviors.md` (Rule 4: open generics are auto-discovered — this memory defines the boundary of "auto").
- Related memory: `parallel-notification-execution` (PatternId `parallel-notification-execution`) — same subsystem; that memory covers notification emission, this one covers behavior expansion.
