# Memory: `[MediatorGeneration(Skip = true)]` Is Inert — Discovery Is Unconditional

## Metadata
- PatternId: mediatorgeneration-skip-inert
- PatternVersion: 1
- Status: active
- Supersedes:
- CreatedAt: 2026-07-12
- LastValidatedAt: 2026-07-12
- ValidationEvidence: `SourceGeneratorDriverTests.ObsoleteMediatorGenerationSkip_IsIgnored_HandlerIsStillDiscovered` (a `Skip = true` handler appears in generated registration; the test failed while the generator still honored the attribute); full suite green.

## Source Context
- Triggering task: Repo-wide bug hunt found the generator still honoring `[MediatorGeneration(Skip = true)]` in all three discovery pipelines while rule `70-testing.md` §3 documented v2 discovery as unconditional — a docs/code contradiction either side could be burned by.
- Scope/system: Source generator discovery (`GetHandlerInfo` / `GetBehaviorInfo` / `GetValidatorInfo` in `HandlerDiscoveryGenerator.cs`), consumer docs (`README.md`, `docs/quick-start.md`, `docs/migration-from-mediatr.md`, `docs/migration-v1-to-v2.md`).
- Date/time: 2026-07-12

## Memory
- Key fact or decision: **The generator ignores `[MediatorGeneration(Skip = true)]` entirely.** Discovery is unconditional for every concrete handler, behavior, and validator in the compilation. The decision (made explicitly by the project owner, choosing code-matches-docs over docs-match-code) removed the three `hasSkipAttribute` checks. The attribute **type** stays in `MediatorLite.Abstractions` with its `[Obsolete]` marking — rule 90 §4 treats obsolete symbols as public-API contract — but it has no behavioral effect.
- Why it matters: This is a rule-90 §3 breaking change (attribute semantics changed). Any consumer who used `Skip = true` while suppressing the obsolete warning will see that handler registered after upgrading. The supported exclusion mechanism is structural: put the type in an assembly the generator does not run on.

## Applicability
- When to reuse: Reviewing PRs that try to reintroduce a per-type opt-out; answering "why is my Skip handler suddenly registered?"; any future proposal for conditional discovery (needs an ADR and a very good reason — conditional discovery is the runtime-configuration sprawl v2 deleted).
- Preconditions/limitations: The attribute type itself must not be deleted outside a major-version bump (rule 90 §4).

## Actionable Guidance
- Recommended future action: If a genuine exclusion need appears, prefer a documented structural convention (separate assembly) over resurrecting the attribute. If the attribute type is ever removed, that is a second, separate rule-90 §3 break.
- Related files/services/components: `src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs` (discovery pipelines), `src/MediatorLite.Abstractions/Abstractions/Attributes.cs` (`MediatorGenerationAttribute`), `docs/migration-v1-to-v2.md` (migration note), `.claude/rules/70-testing.md` §3.
- Related memory: `medl1002-open-behavior-shape` — same subsystem; that memory defines which behavior shapes are discoverable, this one pins that discoverable shapes cannot opt out.
