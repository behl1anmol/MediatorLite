# Memory: v2 Typed-Switch Dispatch Architecture (ValueTask end-to-end)

## Metadata
- PatternId: dispatch-architecture
- PatternVersion: 2
- Status: active
- Supersedes: servicecollectionextensions-options-validation-risks (PatternVersion 1, the v1 reflection/options dispatch & registration model)
- CreatedAt: 2026-06-10
- LastValidatedAt: 2026-06-10
- ValidationEvidence: `dotnet build MediatorLite.sln -c Release` clean (0 warnings); `dotnet test tests/MediatorLite.Tests` 76/76 passing (incl. covariance + parallel-aggregation); benchmarks below.

## Source Context
- Triggering task: Beat MediatR 12.2.0 on every BenchmarkDotNet microbenchmark (latency AND allocations).
- Scope/system: Runtime dispatch path + source generator emission + public mediator surface.
- Date/time: 2026-06-10

## Memory
- **The generated class IS the mediator.** `MediatorLite.SourceGeneration` emits
  `MediatorLite.Generated.SourceGeneratedMediator`, which implements `IMediator`
  directly. There is no runtime `Mediator` wrapper, no `ISourceGeneratedMediator`
  interface, and no `Dictionary<Type, Delegate>` dispatch table. (`Mediator.cs`,
  `NullSourceGeneratedMediator.cs`, and `ISourceGeneratedMediator.cs` were deleted.)
- **Public surface is `ValueTask`-based.** `IMediator.SendAsync<TResponse>` →
  `ValueTask<TResponse>`; `PublishAsync` → `ValueTask`. A request with no behaviors
  whose handler completes synchronously allocates nothing — the per-request method
  returns the handler's `ValueTask` with no async state machine in the mediator.
- **Dispatch is a compile-time C# type-pattern switch** over concrete request /
  notification types, arms emitted **most-derived-first** (preserves `GetType()`
  specificity and keeps the switch compilable when message types inherit).
- **Typed end-to-end, zero boxing.** Per-request `Send_<SafeType>` methods return
  `ValueTask<TConcrete>`. The exact-type result is reinterpreted to
  `ValueTask<TResponse>` via `System.Runtime.CompilerServices.Unsafe.As`, guarded by
  an identity check `typeof(TResponse) == typeof(TConcrete)`. Covariant
  `IRequest<out T>` dispatch (necessarily a reference type) goes through `SlowCast`
  (plain reference cast — never boxes).
- **Registration / lifetime:** generated `AddGeneratedHandlers()` emits
  `services.AddScoped<IMediator, SourceGeneratedMediator>()`. Scoped so the mediator
  captures the resolving scope's `IServiceProvider` (scoped handler deps resolve
  correctly; behaves like a singleton from the root provider). `AddMediatorLite()`
  is now OPTIONAL — `services.TryAddScoped<IMediator, ThrowingMediator>()`, a
  diagnostic fallback that throws `InvalidOperationException` with setup guidance if
  no generator ran. Call order of the two methods does not matter (TryAdd + last-wins).
- **Notifications:** `Publish_<SafeType>` methods return `ValueTask`. Parallel
  strategy starts all handlers as `ValueTask` locals then awaits them (start-all /
  await-all) — no `ArrayPool<Task>`, no `.AsTask()`, no `Task.WhenAll(...ToArray())`,
  zero array allocation, same concurrency + error-strategy semantics. Dispatch
  matches the notification's **runtime type**, fixing a v1 bug where publishing
  through a base/interface-typed reference silently no-oped.

## Why It Matters
- Removes the two-state-machine + dictionary + delegate + boxing overhead that made
  v1 lose to MediatR on latency. Result (this container, MediatR 12.2.0, .NET 10):

  | Scenario | MediatR | MediatorLite v2 | Speedup |
  |---|---|---|---|
  | Simple request | 113 ns / 328 B | 56 ns / 152 B | 2.0x, 0.46x alloc |
  | 1 behavior | 230 ns / 640 B | 107 ns / 280 B | 2.1x, 0.44x alloc |
  | 3 behaviors | 368 ns / 1072 B | 184 ns / 488 B | 2.0x, 0.46x alloc |
  | Notification (seq) | 228 ns / 616 B | 72 ns / 96 B | 3.2x, 0.16x alloc |
  | Notification (par) | 228 ns / 616 B | 70 ns / 96 B | 3.3x, 0.16x alloc |

## Applicability
- Reuse / respect when modifying:
  - `src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs` (emission of
    `SendAsync`/`PublishAsync` switch, `Send_*`/`Publish_*`, registration line).
  - `src/MediatorLite.Abstractions/Abstractions/IMediator.cs` (keep ValueTask).
  - `src/MediatorLite/Configuration/ServiceCollectionExtensions.cs`,
    `src/MediatorLite/Internal/ThrowingMediator.cs`.
- Preconditions/limitations:
  - `Unsafe.As` is only legal under the exact `typeof` guard (identity cast); the
    covariant path must use `SlowCast`.
  - Consumers must consume the returned `ValueTask` exactly once; `.AsTask()` is the
    documented escape hatch for `Task.WhenAll` / fan-out / multi-await.
  - Linear isinst switch is fine to ~50 request types; above that, a
    `FrozenDictionary<Type,int>` + index switch is the deferred (not-yet-built)
    scale lever.

## Actionable Guidance
- Never reintroduce reflection, a runtime wrapper between `IMediator` and the
  generated code, a `Type`-keyed dispatch table, or `Task<object>` type-erased
  delegates. Push any "needs reflection at dispatch" feature into the generator.
- Keep the codified invariants in `.claude/rules/10-dispatch-invariants.md` (Rules
  1–4) and `.claude/rules/00-project-conventions.md` (async surface) in sync with
  any change here.
- `PipelineBehaviorTypeResolver.cs` (v1 runtime behavior-type resolution) was deleted —
  behavior discovery/expansion belongs exclusively to the source generator
  (`ExpandBehaviors`); do not reintroduce runtime resolution.
