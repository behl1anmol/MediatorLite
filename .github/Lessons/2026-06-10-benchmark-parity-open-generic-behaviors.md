# Lesson: Benchmark Parity Must Be Structural, Not Nominal

## Metadata
- PatternId: benchmark-parity
- PatternVersion: 1
- Status: active
- Supersedes:
- CreatedAt: 2026-06-10
- LastValidatedAt: 2026-06-10
- ValidationEvidence: Before/after `dotnet run -c Release --project tests/MediatorLite.Benchmarks`; after the fix, allocations differ per scenario (152/280/488 B) instead of an identical 632 B, and each MediatorLite scenario beats its MediatR baseline.

## Task Context
- Triggering task: Analyse MediatorLite.Benchmarks vs MediatR and make MediatorLite win every scenario.
- Date/time: 2026-06-10
- Impacted area: tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs

## Mistake
- What went wrong: The benchmark's MediatorLite pipeline behaviors were declared as
  **open generic** (`MediatorLiteLoggingBehavior<TRequest, TResponse>` etc.) and all
  three benchmark classes dispatched the **same** `MediatorLiteQuery` request type.
- Expected behavior: The "Simple Request" scenario should run 0 behaviors (matching
  MediatR's 0-behavior setup); "Single Behavior" 1; "Multiple Behaviors" 3.
- Actual behavior: The source generator expands every open-generic behavior to every
  discovered request/response pair at compile time, so `MediatorLiteQuery` always ran
  all **3** behaviors regardless of scenario. The tell: all three Send scenarios
  reported an identical `632 B` allocation. The "simple request" comparison was
  secretly MediatR-0-behaviors vs MediatorLite-3-behaviors — an apples-to-oranges
  benchmark that made MediatorLite look slower than it was.

## Root Cause Analysis
- Primary cause: Open-generic behavior discovery is assembly-wide and compile-time;
  a single shared request type cannot have "0 behaviors" while open-generic behaviors
  exist in the same assembly.
- Contributing factors:
  - The MediatR side controls behavior depth per scenario via `cfg.AddOpenBehavior(...)`
    at registration time (runtime, per-container), so it genuinely had 0/1/3 — but
    MediatorLite cannot scope behaviors to a container, only to types.
  - No assertion tied each scenario's MediatorLite behavior count to its MediatR
    counterpart.
- Detection gap: Identical allocation numbers across structurally-different scenarios
  were not treated as a red flag.

## Resolution
- Fix implemented: Give each scenario its own request type with **closed** behaviors
  targeting exactly that type:
  - `MediatorLiteQuery` → 0 behaviors (MediatorBenchmarks)
  - `MediatorLiteSingleQuery` → 1 closed behavior (PipelineBenchmarks)
  - `MediatorLiteMultiQuery` → 3 closed behaviors (MultipleBehaviorsBenchmarks)
- Why this fix works: Closed behaviors bind to one request type, so each scenario's
  MediatorLite behavior count now exactly matches its MediatR setup. The MediatR side
  is unchanged (still fair).
- Verification performed: Re-ran all four benchmark classes; allocations now diverge
  per scenario and MediatorLite beats MediatR on latency and allocations in all five
  measurements. Documented in `docs/benchmarks.md` with a fairness note.

## Preventive Actions
- Guardrails added: A fairness note in `docs/benchmarks.md` explaining why per-scenario
  request types with closed behaviors are required.
- Tests/checks added: None at the unit level (benchmark-only concern), but the
  `MediatorLiteRegistration.*Count` constants and the REST harness
  `BenchmarkParityGuard` remain the structural-parity mechanism for the API suite.
- Process updates: When comparing source-generated MediatorLite vs runtime-configured
  MediatR, verify behavior/validator/handler **counts per scenario**, not just that
  both "have behaviors".

## Reuse Guidance
- Apply whenever authoring or reviewing MediatorLite-vs-MediatR benchmarks, or any
  benchmark where one side configures pipeline depth at registration time and the
  other at compile time.
- Treat identical allocation/latency across scenarios that should differ structurally
  as a parity bug until proven otherwise.
- Remember: open-generic MediatorLite behaviors apply to **every** request type in the
  assembly; use closed behaviors when you need per-type behavior scoping.
