# Performance discovery — issue draft, 2026-09-04

Draft only. Nothing here has been filed. Review, then file in the order given: the numbering is
the intended execution order, and later issues depend on earlier ones.

**Every claim below is measured.** Raw numbers, environment and caveats:
[`tests/CompetitiveBenchmarks/results/2026-09-04-baseline.md`](../../tests/CompetitiveBenchmarks/results/2026-09-04-baseline.md).
Reproduce with `tests/CompetitiveBenchmarks`. Nothing was filed on the basis of a code smell
alone — two hypotheses I held going in (broken generator incrementality, and a pathology in the
four syntax providers sharing one predicate) were **refuted by measurement and are not issues**.

## The headline

| | vs MediatR | vs Mediator 3.0.2 |
|---|---|---|
| Simple send, diagnostics **off** | **3.1x less overhead** | 3.3x more overhead, and the only one allocating |
| Simple send, **default config** | **35-47% slower** | — |
| Scoped end-to-end | **12% slower** | 1.6x slower |
| Cold start | **23x faster** | **2.6x faster** |

Two things are true at once and both need saying. MediatorLite's **cold start is the best of the
three by a wide margin** and is a genuine architectural asset. Its **steady-state dispatch is
roughly 3x Mediator's overhead**, and **in its out-of-the-box configuration it is slower than
MediatR**, which is the opposite of what the README claims.

## Decomposition of the gap

Per-dispatch overhead above a direct handler call, 1 message type, diagnostics off:

| Component | Cost | Issue |
|---|---|---|
| Transient handler instantiation | 12.0 ns, **all** of the 24 B | #4 |
| DI lookup + type switch + uncached pipeline | 30.3 ns, 0 B | #4, #5 |
| *Mediator's entire overhead, for reference* | *12.9 ns, 0 B* | |
| Default observability, on top of all of the above | **+170 ns, +64 B** | #3 |

**#3 is worth more than every other steady-state issue combined**, and it is the only one that
needs no API change. It should be done first.

## A constraint on every proposed fix

MediatorLite's cold start (5.13 us / 6.85 KB) beats Mediator's (13.45 us / 14.26 KB) precisely
*because* it does not pre-build per-message wrapper objects at container-build time, which is what
Mediator's `CachingMode.Eager` does. **Copying Mediator's eager-singleton-wrapper design wholesale
would trade away the one axis MediatorLite already wins.** Prefer lazy, per-message-type caching.
Every issue below that touches resolution carries a cold-start regression check in its acceptance
criteria.

---

# Phase 0 — Ground truth

## Issue 1 — Add competitive benchmark harness vs Mediator and MediatR
`enhancement`, `Analysis`

Already implemented on `claude/mediatorlite-perf-analysis-bj0sqw` (commits `abb3f3a`, `2064983`).
Filed for tracking, and because every later issue's acceptance criteria cite a benchmark from it.

Four projects under `tests/CompetitiveBenchmarks`, because observability is an assembly-level
opt-out and message count changes the emitted dispatch shape — neither can be varied at runtime.
Each scenario includes a `DirectCall` floor so framework overhead separates from handler cost.

**Open question for the reviewer:** the projects are deliberately **not** in `MediatorLite.sln`,
so CI does not pull MediatR and two third-party mediator packages, and does not run three source
generators over the same assemblies. Cost: they are not built by the main solution and can
bit-rot. Decide whether to add them plus a build-only CI job.

**Acceptance:** all four projects run; `results/` holds a baseline.

---

## Issue 2 — Correct the performance claims in README and docs
`documentation`

Three inaccuracies, all verifiable:

1. **`README.md` claims "O(1) dispatch" and "constant-time handler resolution" in two places.**
   Measured: dispatch cost grows ~0.78 ns per preceding request type (Issue #5). The claim is
   false as written. Either retract it or qualify it, and re-word once #5 lands.
2. **`docs/benchmarks.md` publishes numbers from a configuration it never discloses.**
   `tests/MediatorLite.Benchmarks/AssemblyInfo.cs` sets `[assembly: DisableMediatorLogging]` and
   `[assembly: DisableMediatorTracing]`. Without those two attributes the same send is
   ~4x slower and **slower than MediatR** (Issue #3). A reader who copies the quick-start gets
   the slow path and the documented numbers are unreachable for them.
3. **`docs/benchmarks.md` still documents `tests/MediatorLite.RestApiBenchmarks`**, which
   `CLAUDE.md` records as removed from the repository.

**Acceptance:** no unqualified "O(1)" claim; benchmark docs state the configuration measured and
how to reproduce it; no references to removed projects.

**Note:** this is uncomfortable but cheap, and it should land *before* the perf work rather than
after, so the fixes are not judged against a claim that was never true.

---

# Phase 1 — Steady-state wins, no breaking API change

## Issue 3 — Default observability costs more than the rest of dispatch combined
`bug`, `enhancement` — **highest impact, do first**

**Evidence** (`DefaultDiagnostics`, 1 message type, same assembly and process):

| | Mean | Alloc |
|---|---:|---:|
| Direct handler call | 11.90 ns | 24 B |
| MediatR | 144.88 ns | 224 B |
| MediatorLite, default, null logger | 196.03 ns | 112 B |
| MediatorLite, default, real logger at `Information` | **213.13 ns** | 112 B |
| MediatorLite, diagnostics off (`SmallProject`) | 52.94 ns | 48 B |

The default configuration costs **+170 ns and +64 B** versus the opted-out path, and is
**35-47% slower than MediatR**.

**Mechanism** — `HandlerDiscoveryGenerator.GenerateUnrolledPipeline` emits, per dispatch:

```csharp
var __logger = _sp.GetRequiredService<ILogger<IMediator>>();
__logger.LogDebug("Sending request {RequestType}", "GetUserQuery");
// ... and again on success
```

Two costs, both avoidable:
- `LoggerExtensions.LogDebug(this ILogger, string, params object?[])` materialises an `object[1]`
  **at the call site, before `ILogger.IsEnabled` is consulted**. Two calls = the +64 B. Raising the
  minimum level does **not** remove it — 213.13 ns *is* the filtered-out measurement.
- `ILogger<IMediator>` is resolved from the container on every dispatch.

**Direction** (not prescriptive): guard emission with `if (__logger.IsEnabled(LogLevel.Debug))`;
use a `[LoggerMessage]`-style strongly-typed delegate instead of the `params object[]` overload;
hoist the logger out of the per-call path. Consider whether tracing's per-dispatch
`StartActivity` should also be guarded by `Source.HasListeners()`.

**Acceptance:** `Default_RealLoggerDebugFilteredOut` allocates 0 B above the diagnostics-off path
and lands within noise of it; `Default_NullLogger` beats `MediatR_ForReference`; log output is
unchanged when Debug *is* enabled; the `LogError`-on-exception behaviour (rule `60`) is preserved.

---

## Issue 4 — Handlers are resolved per dispatch and hard-coded Transient
`enhancement`

**Evidence** (`SmallProject`, identical dispatch path, handler registration overridden):

| | Mean | Alloc | Overhead vs floor |
|---|---:|---:|---|
| MediatorLite, Transient (today) | 52.94 ns | 48 B | +42.3 ns / +24 B |
| MediatorLite, Singleton handler | **40.94 ns** | **24 B** | +30.3 ns / **+0 B** |
| Mediator (Singleton by default) | 23.59 ns | 24 B | +12.9 ns / +0 B |

Lifetime alone is worth **12.0 ns and 100% of the per-dispatch allocation**. Publish is worse:
72 B per publish = 3 x 24 B, one instance per notification handler (Issue #6).

**Mechanism** — `GenerateRegistrationCode` emits `services.AddTransient<...>()` for every handler,
behavior and notification handler, with no way to change it. `Mediator`'s default is Singleton and
is configurable; MediatR's is Transient, which is one reason it allocates 224 B.

**This contradicts an existing rule and needs a decision before work starts.** Rule `90` freezes
`AddMediatorLite()` against configuration sprawl, and rule `10` documents the registration
contract. Options:

- **(a) Compile-time assembly attribute** — `[assembly: MediatorServiceLifetime(Singleton)]`,
  resolved by the generator. Keeps the runtime surface frozen, matches the existing
  `[assembly: DefaultNotificationExecution]` precedent, stays source-gen-first. **Recommended.**
- (b) `AddGeneratedHandlers(ServiceLifetime)` overload — generated code, so arguably outside
  rule 90's letter, but against its spirit.
- (c) Leave Transient. Costs 12 ns and all per-dispatch allocation, forever.

Singleton handlers must be stateless and thread-safe; whichever option is chosen, that constraint
needs documenting, and Transient should stay the default unless the reviewer decides otherwise.

**Acceptance:** with the opt-in enabled, simple send allocates 0 B above the floor and publish
allocates 0 B above the floor; default behaviour unchanged when not opted in; cold start does not
regress beyond 5.13 us / 6.85 KB.

---

## Issue 5 — Dispatch is O(n) in the number of request types, not O(1)
`bug`, `enhancement`

**Evidence** (`LargeProject`, 66 request types; positions verified in the emitted
`SourceGeneratedMediator.g.cs` — `MlScale00` is switch arm **#3**, `MlScale63` is arm **#66**):

| | Mean |
|---|---:|
| MediatorLite, arm #3 | 84.82 ns |
| MediatorLite, arm #66 | **133.88 ns** |
| Mediator, first | 38.74 ns |
| Mediator, last | 43.90 ns |

Two structurally identical requests, 63 arms apart, differ by **49.06 ns — about 0.78 ns per
preceding request type**. Mediator's spread over the same distance is 5.16 ns.

Independently, the *same* first-arm send costs 50.02 ns at 1 message type and 86.19 ns at 66
(+36 ns), against Mediator's 23.59 -> 43.00 ns (+19 ns). Both degrade with project size;
MediatorLite roughly twice as much, plus a position penalty Mediator does not have.

**Mechanism** — `GenerateSourceGeneratedMediator` emits one `case ConcreteType r:` arm per request
type. Roslyn compiles type patterns over unrelated reference types as a sequential `isinst` chain;
there is nothing to hash on. The `[MethodImpl(AggressiveInlining)]` on `Send_*` also cannot help a
switch that large.

**Direction** — Mediator's answer is a compile-time threshold (16 messages per kind): emit a switch
below it, a `FrozenDictionary<Type, object>` above. Worth evaluating that shape, and worth
measuring where MediatorLite's own crossover actually falls rather than adopting 16 on faith.

**Acceptance:** at 66 message types the arm-#3 / arm-#66 spread is within noise; no regression at
small message counts (the switch may well still win there); cold start does not regress.

**Depends on:** #1. Pairs naturally with #8.

---

## Issue 6 — Notification publish resolves one instance per handler per publish
`enhancement`

**Evidence** (`LargeProject`, 3 handlers): MediatorLite 88.35 ns / **72 B**; Mediator
17.72 ns / **0 B**; MediatR 396.08 ns / 592 B. The 72 B is exactly 3 x 24 B.

**Mechanism** — the emitted `Publish_*` does `_sp.GetRequiredService<Handler1>()` … per handler per
publish, all Transient. Separately, multi-handler sequential publish is always emitted `async
ValueTask` (`GenerateUnrolledNotificationPublisher`), so the common all-handlers-complete-
synchronously case still builds a state machine; the single-handler case already has a sync fast
path, the multi-handler case does not. Mediator's `ForeachAwaitPublisher` returns a completed
`ValueTask` as soon as everything so far finished synchronously.

**Direction:** cache handler resolution (shares machinery with #4/#5) and add an all-sync fast path
for the multi-handler sequential strategy.

**Acceptance:** publish to N synchronous handlers allocates 0 B above the floor; parallel and
stop-on-first semantics and the rule `40` two-phase invariants are preserved — the existing
`PublishAsync_Parallel_StartPhase_*` / `_AwaitPhase_*` tests must stay green untouched.

---

# Phase 2 — Requires a public API decision

## Issue 7 — Each pipeline behavior allocates a closure and a delegate per request
`enhancement`, **breaking — needs a go/no-go on v3**

**Evidence** (`LargeProject`, 3 no-op behaviors): MediatorLite 223.28 ns / **320 B**; Mediator
46.42 ns / **24 B**; MediatR 438.24 ns / 752 B. Going 0 -> 3 behaviors costs MediatorLite
**+272 B, ~88-91 B per behavior**: one 24 B Transient instance plus one 64 B delegate.

**Mechanism** — `GenerateUnrolledPipeline.BuildPipelineExpression` emits:

```csharp
return b1.HandleAsync(request, () => b2.HandleAsync(request, () => handler.HandleAsync(request, ct), ct), ct);
```

Each lambda captures `request`, `ct` and the next link, so the compiler emits a display class plus
one delegate per behavior, rebuilt on every request.

Mediator reaches 0 B by (a) folding the chain into one cached `MessageHandlerDelegate` at
container-build time, and (b) a delegate signature that **takes the message and token as
parameters** so nothing needs capturing:

```csharp
public delegate ValueTask<TResponse> MessageHandlerDelegate<TMessage, TResponse>(TMessage message, CancellationToken ct);
```

**The blocker.** MediatorLite's `RequestHandlerDelegate<TResponse>()` takes no parameters, so
capture is unavoidable. Changing it breaks `IPipelineBehavior` for **every consumer behavior ever
written**, and rule `30` fixes that contract while rule `90` requires an ADR, a lesson and a
migration-doc entry for the break.

**Decision needed:** is a v3 on the table? If not, this issue should be closed as won't-fix and the
pipeline gap to Mediator accepted — part (a) alone (caching the chain) still helps latency but
cannot reach 0 B while the closure exists.

**Acceptance (if approved):** 3 behaviors allocate 0 B above the floor; ADR under
`.github/Memories/`; `docs/migration-v1-to-v2.md` extended (do not start a new file); short-circuit
and `[BehaviorOrder]` semantics and the validation-outermost invariant (rule `50`) preserved.

---

## Issue 8 — No monomorphized concrete-typed send path
`enhancement` — additive, non-breaking

**Evidence:** `Mediator_ConcreteClass` 14.04 ns (+3.4 ns over the floor) vs `Mediator_IMediator`
23.59 ns (+12.9 ns). Roughly a 4x reduction in overhead for callers whose request type is
statically known. MediatorLite has no equivalent — `IMediator.SendAsync(IRequest<TResponse>)` is
the only entry point, so every call pays the type switch even when the compiler knew the type.

**Direction:** emit `public ValueTask<Response> SendAsync(ConcreteRequest request, CancellationToken)`
overloads on `SourceGeneratedMediator`, selected by ordinary overload resolution for callers that
inject the concrete class. Purely additive; `IMediator` is untouched.

**Trade-off to weigh, not assume:** injecting the concrete generated class couples consumer code to
`MediatorLite.Generated`, which rule `90` currently reserves. Whether that is worth ~9 ns is a
judgement call for the maintainer, and it interacts with #5 — if #5 makes the interface path O(1),
the remaining benefit shrinks. **Measure after #5, decide then.**

---

# Phase 3 — Parity and capability

## Issue 9 — Acceptance gate: scoped end-to-end must beat MediatR
`Analysis` — tracking issue for #3-#7

**Evidence** (`LargeProject`, create scope -> resolve `IMediator` -> send -> dispose): MediatR
307.6 ns / 456 B; **MediatorLite 342.9 ns / 456 B (ratio 1.12)**; Mediator 212.0 ns / 224 B.

This is the realistic ASP.NET Core per-request shape and the one number a user is most likely to
feel. It is a *consequence* of #3-#7 rather than a separate defect, so it exists to hold the work
to an outcome rather than to a set of micro-optimisations.

Caveat to keep in the issue: scope creation (~200 ns) is framework cost common to all three and
largely cancels; in a real app the scope is created by ASP.NET Core and `IMediator` is injected.
The MediatorLite-vs-Mediator *difference* is the meaningful part.

**Close when:** `MediatorLite_Scoped` beats `MediatR_Scoped` on mean and allocation, and the gap to
`Mediator_Scoped` is documented with whatever remains explained.

---

## Issue 10 — No streaming support (`IStreamRequest` / `CreateStream`)
`enhancement` — **scope question, not a performance claim**

Mediator supports streaming end to end: `IStreamRequest<T>` / `IStreamQuery<T>` /
`IStreamCommand<T>`, `IStreamPipelineBehavior<,>`, and `CreateStream` returning
`IAsyncEnumerable<T>`, with the same wrapper-caching treatment as requests. MediatorLite has none.

No number is offered here — this is a feature gap, and filing it as a performance issue would be
dishonest. It belongs on the roadmap only if streaming is a goal for MediatorLite; "lightweight" is
a legitimate reason to decline. **Maintainer decision, not a recommendation.**

---

## Issue 11 — AOT and trimming are neither declared nor verified
`enhancement`

`src/MediatorLite/MediatorLite.csproj` and `MediatorLite.Abstractions.csproj` set neither
`IsAotCompatible` nor `IsTrimmable`. Mediator sets `IsAotCompatible` for all non-netstandard TFMs
and treats Native AOT cold start as a first-class scenario.

MediatorLite's architecture looks well suited to this — zero reflection, closed-generic
registrations only — so the gap is likely declaration and CI verification rather than
implementation. **That is a hypothesis, not a measurement:** it must be verified with an actual
`PublishAot` run before any claim is made publicly.

Given #1's finding that MediatorLite's cold start is already the best of the three, an AOT story is
the natural place to press that advantage.

**Acceptance:** `IsAotCompatible` set where it holds; a sample publishes with `PublishAot=true` and
runs; CI covers it; only then may the README mention AOT.

---

# Not filed — hypotheses refuted by measurement

Recorded so they are not re-raised.

- **Generator incrementality is fine.** An edit adding a syntax tree with no MediatorLite types
  never re-executes the output node, at 50 / 200 / 800 handlers. The deliberate projection of
  `CompilationProvider` into a `bool` in `Initialize` works as intended.
- **No pathology in generator throughput.** Cold generation scales sub-linearly: 16x the handlers
  costs 8.6x the time (19.4 -> 167.7 ms, median of 7). The four syntax providers sharing one
  predicate did not show up as a problem. Not worth touching on current evidence.
