# Lesson: Generator Discovery Guards Must Be Shared Across Every Pipeline

## Metadata
- PatternId: generator-discovery-guards-must-be-shared
- PatternVersion: 1
- Status: active
- Supersedes:
- CreatedAt: 2026-07-12
- LastValidatedAt: 2026-07-12
- ValidationEvidence: `SourceGeneratorDriverTests.GenericHandlerClass_WithClosedInterface_ReportsMedl1004_AndIsNotEmitted`, `OpenGenericHandlers_ReportMedl1004_AndAreNotEmitted`, `HandlerNestedInGenericOuterType_ReportsMedl1004_AndIsNotEmitted`, `BehaviorNestedInGenericOuterType_ReportsMedl1002_AndIsNotEmitted` (each fails on the pre-fix generator — no diagnostic, non-compiling `.g.cs` — and passes after); full suite 125/125; clean `dotnet build MediatorLite.sln -c Release` with warnings-as-errors.

## Task Context
- Triggering task: Repo-wide adversarial bug hunt. The generator reviewer flagged that `GetHandlerInfo` had no generic-type guard even though `GetValidatorInfo` skipped `IsGenericType` and `GetBehaviorInfo` reported MEDL1002 — so generic handlers emitted unbound type parameters and broke the consumer's build.
- Date/time: 2026-07-12
- Impacted area: `src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs` — the four discovery transforms (`GetHandlerInfo`, `GetBehaviorInfo`, `GetValidatorInfo`, and the notification handler path inside `GetHandlerInfo`).

## Mistake
- What went wrong: Each discovery pipeline grew its own "is this type emittable?" check independently, and each had a *different* hole:
  1. `GetHandlerInfo` (request + notification handlers) had **no** guard at all — a generic handler class, an open generic handler (`IRequestHandler<TReq, TRes>`), or a handler nested inside a generic outer type had its fully-qualified display name (carrying unbound type parameters) pasted into the generated registration and dispatch switch.
  2. `GetValidatorInfo` guarded with `classSymbol.IsGenericType`, which catches a generic *class* but **not** a non-generic validator nested inside a generic outer type (`Outer<T>.InnerValidator`), whose display name still carries `T`.
  3. `GetBehaviorInfo` / `IsSupportedOpenShape` checked only the class's own `TypeParameters`, so a closed-interface behavior nested in a generic outer type slipped through, and the open-shape path additionally truncated the display name at the outer type's `<`, emitting the wrong type name entirely.
- Expected behavior: Any type whose fully-qualified name would contain an unbound type parameter is excluded from emission and surfaced as a diagnostic (MEDL1002 for behaviors, new MEDL1004 for handlers), never emitted as non-compiling code.
- Actual behavior: Generic/nested-generic handlers emitted `case TReq r_TReq:` and `Ns.Handler<TUnused>` registrations, breaking the *consumer's* build with CS0246/CS0103 inside a `.g.cs` file they cannot edit — with no diagnostic pointing at the cause.

## Root Cause Analysis
- Primary cause: The invariant "an emitted type's display name must contain no unbound type parameter" is identical for handlers, behaviors, and validators, but it was expressed three times, three different ways, by whoever last touched each pipeline. Copy-drift guaranteed the guards diverged.
- Contributing factors: `INamedTypeSymbol.IsGenericType` reads as "covers generics" but only inspects the type itself, not its containing-type chain — a subtle gap that looks correct in review. The behavior path's openness check was written for the interface arguments, so nobody thought to also check the *containing* type.
- Detection gap: No driver test drove the generator with a generic or nested-in-generic handler/behavior/validator and asserted the output compiles; the existing MEDL1002 tests only covered the class's own type parameters.

## Resolution
- Fix implemented: Added one shared helper, `HasTypeParametersInScope(INamedTypeSymbol)`, that walks the `ContainingType` chain and returns true if the type *or any enclosing type* declares type parameters. All three discovery pipelines now use it: `GetHandlerInfo` flags such classes and `Execute` reports the new warning **MEDL1004** and excludes them; `GetBehaviorInfo`/`IsSupportedOpenShape` reject nested-in-generic types into the existing MEDL1002; `GetValidatorInfo` replaces its `IsGenericType` check with the shared helper.
- Why this fix works: A single predicate expresses the invariant once, so the three pipelines cannot drift again; walking the containing-type chain closes the nested-generic hole that `IsGenericType` and the own-`TypeParameters`-only checks both missed.
- Verification performed: The four driver tests above (each fails on the pre-fix generator, passes after), plus the full suite and a warnings-as-errors Release build.

## Preventive Actions
- Guardrails added: `HasTypeParametersInScope` carries a doc comment stating it is *the* emittability guard and why the containing-type chain matters; MEDL1004 registered in `AnalyzerReleases.Unshipped.md`.
- Tests/checks added: `SourceGeneratorDriverTests` gained four tests spanning generic class, open generic handler, handler-nested-in-generic, and behavior-nested-in-generic, each asserting the diagnostic is present AND `AssertGeneratedOutputCompiles`.
- Process updates: Any new discovery pipeline (rule `20-source-generator.md`) that emits a type's display name MUST route the candidate through `HasTypeParametersInScope` and skip-with-diagnostic on a true result — never add a bespoke `IsGenericType`/`TypeParameters.Length` check.

## Reuse Guidance
- How to apply this lesson in future tasks: When a generator has parallel discovery/emission paths that share an invariant (here: "no unbound type parameter in the emitted name"), express the invariant as one shared predicate and call it from every path — divergent copies are where the holes hide. Whenever you emit `ITypeSymbol.ToDisplayString(FullyQualifiedFormat)`, ask "could this contain a type parameter?" and remember that `IsGenericType` does not account for nesting inside a generic type. Every emittability guard needs a driver test that asserts the generated output actually compiles, not just that a diagnostic fires.
