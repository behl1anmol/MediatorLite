# Lesson: Incremental Generator Models Must Have Value Equality End-to-End

## Metadata
- PatternId: incremental-generator-model-equality
- PatternVersion: 1
- Status: active
- Supersedes:
- CreatedAt: 2026-07-02
- LastValidatedAt: 2026-07-02
- ValidationEvidence: `SourceGeneratorDriverTests.UnrelatedEdit_LeavesGeneratorOutputsCached` (runs the generator twice via `CSharpGeneratorDriver` with `trackIncrementalGeneratorSteps: true`, applies an unrelated edit, and asserts every tracked output step reason is `Cached`/`Unchanged`); full suite 87/87; clean `dotnet build MediatorLite.sln -c Release` with warnings-as-errors.

## Task Context
- Triggering task: Repo-wide adversarial bug hunt; generator reviewer flagged that `HandlerDiscoveryGenerator` regenerated both source files on every keystroke despite being an `IIncrementalGenerator`.
- Date/time: 2026-07-02
- Impacted area: `src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs` (pipeline wiring + model records), new `src/MediatorLite.SourceGeneration/EquatableArray.cs`.

## Mistake
- What went wrong: Two independent defects each fully defeated incremental caching:
  1. Pipeline model records (`HandlerInfo`, `HandlerInterfaceInfo`, `NotificationHandlerInterfaceInfo`, `BehaviorInfo`) carried `List<>` members. Synthesized record equality compares collection members with `EqualityComparer<List<T>>.Default` — **reference equality** — and the transforms allocate fresh lists every run, so two structurally identical models never compared equal.
  2. The final combine folded the raw `CompilationProvider` into the output node. A `Compilation` is a new instance on every edit, so `RegisterSourceOutput` re-ran regardless of model caching. The only compilation-level fact `Execute` needed was "is `MediatorLite.FluentValidation.FluentValidationBehavior`2` referenced".
- Expected behavior: An edit that changes no discovered handler/behavior/validator leaves both generated files cached (rule `20-source-generator.md` §1).
- Actual behavior: Every keystroke in any file of a consuming compilation re-ran `Execute` and re-emitted `MediatorLiteRegistration.g.cs` + `SourceGeneratedMediator.g.cs`.

## Root Cause Analysis
- Primary cause: Records give the *appearance* of value equality, but that guarantee stops at collection members — `List<T>` inside a record is compared by reference.
- Contributing factors: Combining `CompilationProvider` directly is the documented anti-pattern in the incremental-generators cookbook, but it "works" functionally so nothing flagged it; the syntax-provider predicates being correct made the pipeline look incremental at a glance.
- Detection gap: No test exercised the generator through `GeneratorDriver` with `trackIncrementalGeneratorSteps`, so cacheability regressions were invisible — the generator's *output* is identical whether or not caching works.

## Resolution
- Fix implemented: Added `EquatableArray<T>` (readonly struct, sequence equality + matching hash, `IEnumerable<T>` so existing LINQ keeps working) and replaced every `List<>` member of the model records with it; replaced the `CompilationProvider` combine with a projected provider — `Select(c => c.GetTypeByMetadataName("MediatorLite.FluentValidation.FluentValidationBehavior`2") is not null)` — so `Execute` takes a stable `bool` instead of the `Compilation`.
- Why this fix works: Model equality is now structural at every level, so unchanged transforms are recognized as cached; the projected bool is value-equal across edits, so the output node's inputs only change when a discovered model or the FluentValidation reference actually changes.
- Verification performed: The cacheability driver test above (fails on the old code, passes on the new), plus the full test suite and Release build.

## Preventive Actions
- Guardrails added: `EquatableArray<T>` doc comment explains *why* it exists; a comment block above the model records forbids `List<>` members.
- Tests/checks added: `tests/MediatorLite.Tests/UnitTests/SourceGeneratorDriverTests.cs` — `UnrelatedEdit_LeavesGeneratorOutputsCached` pins cacheability permanently.
- Process updates: Any new field on a pipeline model record must itself have value equality (string, primitive, record, or `EquatableArray<T>`); any new compilation-level fact must be projected to an equatable value via `Select` before combining, never passed as the `Compilation`.

## Reuse Guidance
- How to apply this lesson in future tasks: When touching `Initialize()` or the model records, ask two questions — (1) does every model member compare by value? (2) does anything downstream of `RegisterSourceOutput` receive a `Compilation`, `ISymbol`, or `SyntaxNode`? If either answer is wrong, caching is silently dead even though builds stay green; only a `trackIncrementalGeneratorSteps` driver test can prove it. Applies to any Roslyn incremental generator, not just this repo.
