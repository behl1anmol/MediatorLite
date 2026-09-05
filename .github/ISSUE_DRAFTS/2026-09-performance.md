# Performance discovery — 2026-09-04

**Filed as #31-#41 on 2026-09-04.** This document is the working source; the GitHub issues are
self-contained copies. Edit here and mirror changes to the issue, or edit the issue directly and
treat this as historical — pick one, do not let them drift silently.

| Draft | Issue | Title |
|---|---|---|
| 1 | [#31](https://github.com/behl1anmol/MediatorLite/issues/31) | Add competitive benchmark harness |
| 2 | [#32](https://github.com/behl1anmol/MediatorLite/issues/32) | Correct the performance claims in README and docs |
| 3 | [#33](https://github.com/behl1anmol/MediatorLite/issues/33) | Default observability costs +170 ns / +64 B |
| 4 | [#34](https://github.com/behl1anmol/MediatorLite/issues/34) | [SPIKE] Handler lifetime hard-coded Transient |
| 5 | [#35](https://github.com/behl1anmol/MediatorLite/issues/35) | Dispatch is O(n), not O(1) |
| 6 | [#36](https://github.com/behl1anmol/MediatorLite/issues/36) | Notification publish resolves per handler per publish |
| 7 | [#37](https://github.com/behl1anmol/MediatorLite/issues/37) | [BREAKING/v3] RequestHandlerDelegate carries the request |
| 8 | [#38](https://github.com/behl1anmol/MediatorLite/issues/38) | No monomorphized concrete-typed send path |
| 9 | [#39](https://github.com/behl1anmol/MediatorLite/issues/39) | Acceptance gate: scoped end-to-end must beat MediatR |
| 10 | [#40](https://github.com/behl1anmol/MediatorLite/issues/40) | No streaming support — scope decision |
| 11 | [#41](https://github.com/behl1anmol/MediatorLite/issues/41) | AOT and trimming neither declared nor verified |

The numbering is the intended execution order, and later issues depend on earlier ones.

**Every claim below is measured.** Raw numbers, environment and caveats:
[`tests/CompetitiveBenchmarks/results/2026-09-04-baseline.md`](../../tests/CompetitiveBenchmarks/results/2026-09-04-baseline.md).
Reproduce with `tests/CompetitiveBenchmarks`. Nothing was filed on the basis of a code smell
alone — two hypotheses held going in (broken generator incrementality, and a pathology in the
four syntax providers sharing one predicate) were **refuted by measurement and were deliberately
not filed**.

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

Two issues carry decisions rather than proposals. **#4 is a spike**: the measurement is settled,
the design is not, so it lists every option with expected code changes and a recommendation, and
its deliverable is a recorded decision. **#7 is an approved breaking change**: the maintainer has
green-lit a v3 in principle, so it carries full ADR, lesson and docs drafts to be revisited before
implementation.

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

The four projects are in `MediatorLite.sln` and are built by a dedicated
`Competitive Benchmarks (build only)` CI job, so a refactor in `src/` cannot silently break the
harness. CI never runs the benchmarks: a sweep takes tens of minutes and shared runners are too
noisy for numbers worth keeping. Verified locally that the strict `code-quality` job
(`dotnet format whitespace/style --verify-no-changes` plus
`/p:EnforceCodeStyleInBuild=true /p:TreatWarningsAsErrors=true`) passes with them included.

**Known gap:** building is not running. A csproj filename that did not match `<AssemblyName>` broke
BenchmarkDotNet's project resolution at *runtime* while still building cleanly — build-only CI
would not have caught it. Consider adding a `--job Dry` smoke step later if that recurs.

**Acceptance:** all four projects run; `results/` holds a baseline; CI builds them.

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

# Phase 1 — Steady-state wins (no breaking change to existing API)

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

## Issue 4 — [SPIKE] Handler lifetime is hard-coded Transient; evaluate making it configurable
`Analysis`, `enhancement` — **spike: decide the approach before any implementation**

This is deliberately a spike, not an implementation ticket. The measurement is settled; the design
is not, because every option collides with rule `90` to a different degree. Deliverable is a
decision recorded on this issue, then a separate implementation issue.

### Measurement (settled)

`SmallProject`, identical generated dispatch path, only the handler registration differs:

| | Mean | Alloc | Overhead vs direct call |
|---|---:|---:|---|
| MediatorLite, Transient (today) | 52.94 ns | 48 B | +42.3 ns / +24 B |
| MediatorLite, Singleton handler | **40.94 ns** | **24 B** | +30.3 ns / **+0 B** |
| Mediator (Singleton by default) | 23.59 ns | 24 B | +12.9 ns / +0 B |
| Direct handler call (floor) | 10.66 ns | 24 B | — |

**Lifetime alone is worth 12.0 ns and 100% of the per-dispatch allocation.** Publish is worse:
72 B per publish, exactly 3 x 24 B for three handlers.

**But note the ceiling.** Even with Singleton handlers MediatorLite sits at +30.3 ns against
Mediator's +12.9 ns, so this fixes 28% of the latency gap and all of the allocation gap. It is not
on its own sufficient. Sequence it with #5 and #6, which attack the remaining 30.3 ns.

### Current behaviour

`HandlerDiscoveryGenerator.GenerateRegistrationCode` emits, with no way to influence it:

```csharp
services.AddTransient<IRequestHandler<GetUserQuery, UserDto>, GetUserQueryHandler>();
services.AddTransient<INotificationHandler<UserCreated>, AuditHandler>();
services.AddTransient<AuditHandler>();                     // concrete, for unrolled publish
services.AddTransient<SomeBehavior>();                     // concrete, for unrolled pipeline
```

For comparison: Mediator defaults to Singleton and lets you change it; MediatR is Transient, which
is one reason it allocates 224 B.

### Options

---

**Option A — compile-time assembly attribute (recommended)**

```csharp
// consumer's AssemblyInfo.cs
[assembly: MediatorLite.MediatorServiceLifetime(MediatorServiceLifetime.Singleton)]
```

New no-arg-style attribute in `MediatorLite.Abstractions/Abstractions/Attributes.cs`, read by the
generator exactly as `DefaultNotificationExecutionAttribute` already is:

```csharp
// Attributes.cs — new
public enum MediatorServiceLifetime { Transient = 0, Scoped = 1, Singleton = 2 }

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class MediatorServiceLifetimeAttribute(MediatorServiceLifetime lifetime) : Attribute
{
    public MediatorServiceLifetime Lifetime { get; } = lifetime;
}
```

```csharp
// HandlerDiscoveryGenerator.GetAssemblyDefaults — extend the existing scan, ~6 lines
else if (IsMediatorLiteAttribute(attr, "MediatorServiceLifetimeAttribute")
    && attr.ConstructorArguments.Length > 0
    && attr.ConstructorArguments[0].Value is int lt)
{
    lifetime = lt;
}
```

```csharp
// GenerateRegistrationCode — replace the hard-coded "AddTransient" literal
var add = defaults.Lifetime switch { 2 => "AddSingleton", 1 => "AddScoped", _ => "AddTransient" };
sb.AppendLine($"            services.{add}<{iface.InterfaceType}, {handler.ClassName}>();");
```

`AssemblyDefaults` gains one `int` field; it already flows to `Execute` and would need threading
into `GenerateRegistrationCode`, which currently does not receive it.

- **Rule 90:** untouched. `AddMediatorLite()` stays argument-free; no `MediatorOptions` returns.
- **Consistent** with the existing compile-time-strategy philosophy (rule `40`).
- **AOT/trimming friendly** — resolved at compile time, nothing to read at runtime.
- **Cost:** one new public attribute + enum in Abstractions (additive, non-breaking).
- **Caveat:** Singleton handlers must be stateless and thread-safe, and must not capture scoped
  dependencies. A Singleton handler injecting a `DbContext` is a captive-dependency bug. Needs
  loud documentation, and consider having the generator emit a diagnostic (`MEDL1006`?) when a
  Singleton-configured handler's constructor takes a known-scoped service — investigate whether
  that is detectable at compile time.

---

**Option B — `AddGeneratedHandlers(ServiceLifetime)` overload**

```csharp
services.AddGeneratedHandlers(ServiceLifetime.Singleton).AddMediatorLite();
```

Generated code takes the lifetime as a parameter and passes it to `services.Add(new ServiceDescriptor(...))`.

- **Simplest to implement**, and familiar to MediatR/Mediator users.
- **Against rule 90's spirit**: puts a runtime configuration knob back on the registration path,
  which is exactly the sprawl v2 deleted. `AddGeneratedHandlers` is generated code rather than the
  frozen `AddMediatorLite()`, so it escapes the letter of the rule but not its intent.
- Lifetime becomes invisible to the generator, so no compile-time diagnostics are possible and the
  emitted dispatch cannot be specialised for it (rules out caching wins in #5 that depend on
  knowing statically that a handler is a Singleton).

---

**Option C — keep Transient, close the issue**

- Rule 90 stays absolutely intact; zero new public surface.
- Costs 12 ns and 24 B per send and 24 B per notification handler per publish, permanently.
- Makes the 0 B targets in #6 and #7 unreachable, so it partly forecloses those issues too.

---

**Option D — Singleton by default, opt *out***

Matches Mediator, maximises the default-path win, and is the only option that helps users who
never read the docs.

- **Breaking behavioural change** for anyone whose handler holds per-request state or injects a
  scoped dependency — and it breaks *silently*, at runtime, under concurrency. Given MediatorLite
  already ships v2 to real consumers, this is the highest-risk option. **Not recommended outside a
  major version**, and even then, pair it with the captive-dependency diagnostic from Option A.

### Recommendation

**Option A**, with Transient remaining the default. It buys the full allocation win for consumers
who opt in, keeps rule 90 intact, matches the codebase's compile-time philosophy, and is the only
option that leaves the door open for the generator to specialise dispatch on a known lifetime,
which #5 may want.

### Spike deliverables

1. Decision recorded here with rationale, plus an ADR under `.github/Memories/` if Option A, B or D.
2. Confirm whether a captive-dependency diagnostic is feasible from the generator's semantic model.
3. Confirm the interaction with #5: does knowing the lifetime at compile time unlock caching that
   is otherwise unavailable? Answer before implementing either.
4. Prototype behind the chosen option and re-run `SmallProject` and `LargeProject` to confirm the
   12 ns / 24 B, and confirm cold start does not regress beyond 5.13 us / 6.85 KB.

**Acceptance (for the follow-up implementation issue, not this spike):** opted-in simple send and
publish allocate 0 B above the direct-call floor; default behaviour unchanged when not opted in;
cold start not regressed; captive-dependency risk documented.

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

# Phase 2 — Public API changes

## Issue 7 — Change `RequestHandlerDelegate` to carry the request, removing per-behavior closures
`enhancement`, **breaking change — v3 candidate, approved for drafting**

Maintainer has approved a breaking change in principle. This issue carries the full drafts (ADR,
lesson, docs, code) so they can be revisited and edited before any implementation starts.

### Measurement

`LargeProject`, three no-op behaviors:

| | Mean | Alloc |
|---|---:|---:|
| MediatR | 438.24 ns | 752 B |
| **MediatorLite** | **223.28 ns** | **320 B** |
| Mediator | 46.42 ns | **24 B** |

Going 0 -> 3 behaviors costs MediatorLite **+272 B, ~88-91 B per behavior**: one 24 B Transient
instance plus one ~64 B delegate. Mediator adds **0 B**.

### Mechanism

`GenerateUnrolledPipeline.BuildPipelineExpression` emits a nested lambda chain:

```csharp
return b1.HandleAsync(request, () => b2.HandleAsync(request, () => handler.HandleAsync(request, ct), ct), ct);
```

Each lambda closes over `request`, `ct` and the next link, so the compiler emits a display class
plus one delegate per behavior, **rebuilt on every request**. The root cause is the delegate's
signature: `RequestHandlerDelegate<TResponse>()` takes no parameters, so there is nothing to pass
the request and token through — capture is the only option available.

### Honest scope: this issue is an enabler, not the whole win

Changing the signature removes the display class and makes the chain *cacheable*. It does **not**
by itself reach 0 B: `b1.HandleAsync(request, handler.HandleAsync, ct)` still converts a method
group to a delegate per call. Reaching 0 B additionally requires the folded chain to be built once
and stored, which requires stable behavior and handler instances — i.e. **#4** (lifetime) and
**#5**/#6 (cached resolution).

Sequence: **#4 -> #7 -> chain caching**. Do not file the 0 B target against this issue alone.

### Proposed API change

```csharp
// BEFORE — src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs
public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default);
}
```

```csharp
// AFTER
public delegate ValueTask<TResponse> RequestHandlerDelegate<TRequest, TResponse>(
    TRequest request,
    CancellationToken cancellationToken)
    where TRequest : IRequest<TResponse>;

// NOTE: `in` must be dropped from TRequest. TRequest now appears inside a delegate that is
// itself a method parameter, which flips its variance position; `in TRequest` no longer
// compiles. That is a second, separate breaking change for anyone relying on contravariance.
public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default);
}
```

### Consumer migration — mechanical, two edits per behavior

```csharp
// BEFORE
public sealed class LoggingBehavior : IPipelineBehavior<GetUserQuery, UserDto>
{
    public async ValueTask<UserDto> HandleAsync(
        GetUserQuery request,
        RequestHandlerDelegate<UserDto> next,            // 1. add TRequest
        CancellationToken cancellationToken = default)
        => await next();                                  // 2. pass request + token
}

// AFTER
public sealed class LoggingBehavior : IPipelineBehavior<GetUserQuery, UserDto>
{
    public async ValueTask<UserDto> HandleAsync(
        GetUserQuery request,
        RequestHandlerDelegate<GetUserQuery, UserDto> next,
        CancellationToken cancellationToken = default)
        => await next(request, cancellationToken);
}
```

Short-circuiting is unaffected: a behavior that never calls `next` keeps working unchanged
(rule `30` Rule 3).

**Consider shipping a Roslyn analyzer + code fix** for the migration. Both edits are purely
syntactic and the compiler already locates every site via CS1501/CS0305. Worth scoping as a
sub-issue — it is the difference between a 10-minute upgrade and an afternoon for a large consumer.

### Generator change

`GenerateUnrolledPipeline.BuildPipelineExpression` — replace lambda nesting with method-group
composition, then (after #4) hoist the fold into a cached field:

```csharp
// Stage 1 — no display class, N method-group delegates remain
return b1.HandleAsync(request, b2.HandleAsync, ct);   // for the 1-inner-behavior case
// general case still needs intermediate delegates; emit them, then:

// Stage 2 (requires #4 stable instances) — fold once, cache per request type
private RequestHandlerDelegate<Foo, Result>? _pipeline_Foo;
private ValueTask<Result> Send_Foo(Foo request, CancellationToken ct)
    => (_pipeline_Foo ??= BuildPipeline_Foo())(request, ct);
```

An alternative worth evaluating rather than assuming: **generic struct composition**, where each
pipeline step is a `readonly struct` implementing a step interface, so the chain is a stack of
value types with no delegate at all. Mediator did not do this. It trades allocation for generic
instantiation count and code size, and could hurt the cold-start advantage. Measure before choosing.

### In-repo blast radius (measured)

- **107** references to `RequestHandlerDelegate` across **43** files
- **30** `next()` call sites
- **10** files containing `IPipelineBehavior<...>` implementations

Note that the agent-instruction trees are **mirrored three ways** — `.claude/`, `.agents/`,
`.cursor/` all carry copies of `30-pipeline-behaviors.md`, `add-new-pipeline-behavior.md` and the
skill files. All three must be updated together or the agents will keep generating v2 behaviors.

Code that must change: `src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs`,
`src/MediatorLite.FluentValidation/FluentValidationBehavior.cs`,
`src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs`,
`samples/MediatorLite.Sample.SourceGen/Behaviors/*.cs` (2),
`tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs`,
`tests/MediatorLite.Tests/UnitTests/SourceGeneratorDriverTests.cs`,
`tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs`,
`tests/CompetitiveBenchmarks/LargeProject/*.cs`.

### Draft ADR — `.github/Memories/pipeline-delegate-carries-request.md`

Matches the repo's existing memory template. **Draft: dates, PatternId and evidence must be filled
in at implementation time.**

```markdown
# Memory: `RequestHandlerDelegate` Carries the Request and Token

## Metadata
- PatternId:            pipeline-delegate-carries-request
- PatternVersion:       1
- Status:               active
- Supersedes:
- CreatedAt:            <YYYY-MM-DD>
- LastValidatedAt:      <YYYY-MM-DD>
- ValidationEvidence:   `tests/CompetitiveBenchmarks/LargeProject` PipelineBench — 3 behaviors
                        allocate 0 B above the DirectCall floor (was 320 B vs a 24 B floor);
                        baseline `tests/CompetitiveBenchmarks/results/2026-09-04-baseline.md` §4.

## Source Context
- Triggering task:      Performance discovery 2026-09; MediatorLite's 3-behavior pipeline measured
                        223.28 ns / 320 B against martinothamar/Mediator's 46.42 ns / 24 B.
- Scope/system:         `src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs`,
                        `HandlerDiscoveryGenerator.GenerateUnrolledPipeline`,
                        `src/MediatorLite.FluentValidation/FluentValidationBehavior.cs`.
- Date/time:            <YYYY-MM-DD>

## Memory
- Key fact or decision: `RequestHandlerDelegate<TResponse>()` became
  `RequestHandlerDelegate<TRequest, TResponse>(TRequest, CancellationToken)`, and
  `IPipelineBehavior` lost `in` on `TRequest`. The parameterless delegate forced every generated
  pipeline arm to capture `request`/`ct`/next in a compiler display class, costing ~88 B per
  behavior per request. Passing them as arguments removes the capture and makes the folded chain
  cacheable.
- Why it matters: This is the only route to a 0 B pipeline. It is a rule 90 §3 breaking change
  (public delegate signature + interface variance) and every consumer behavior must be edited.

## Applicability
- When to reuse: Any proposal to reintroduce a parameterless `next`, or to add a
  `RequestHandlerDelegate<TResponse>` overload "for convenience" — that overload reintroduces the
  capture and silently undoes this work.
- Preconditions/limitations: The 0 B result additionally requires stable behavior/handler
  instances (see `PatternId: mediator-handler-lifetime`) and a cached fold. The signature change
  alone removes the display class but leaves one method-group delegate per behavior.

## Actionable Guidance
- Recommended future action: Keep `next` parameterised. Reject convenience overloads.
- Related files/services/components: `IPipelineBehavior.cs`, `HandlerDiscoveryGenerator.cs`
  (`BuildPipelineExpression`), `FluentValidationBehavior.cs`, `.claude/rules/30-pipeline-behaviors.md`
  and its `.agents/` and `.cursor/` mirrors.

## Context and Problem Statement
Pipeline behaviors allocated ~88 B per behavior per request, entirely from a compiler-generated
display class plus one delegate, rebuilt on every dispatch.

## Considered Options
1. **Parameterise the delegate** (chosen) — matches martinothamar/Mediator; the only option that
   removes the capture; breaking.
2. **Cache the closure chain without changing the signature** — non-breaking, removes the
   per-request rebuild, but the display class still exists and instances must be stable anyway.
   Rejected as insufficient: cannot reach 0 B.
3. **Generic struct composition** — no delegate at all; rejected for now on code-size and
   cold-start risk, and because it is unproven in this space. Revisit if #4/#5 land and the
   remaining delegate cost still matters.
4. **Do nothing** — rejected; leaves a ~7x pipeline gap to Mediator.

## Decision Outcome
Option 1, gated behind a major version. Ship a Roslyn code fix alongside if feasible.

## Consequences
- Every consumer `IPipelineBehavior` implementation must be edited (two mechanical changes).
- Contravariance on `TRequest` is lost.
- Enables the cached-fold work; without that, the win is partial.
```

### Draft lesson — `.github/Lessons/<date>-pipeline-closure-allocation.md`

```markdown
# Lesson: A Parameterless `next` Delegate Forces a Per-Request Closure

## Metadata
- PatternId: pipeline-parameterless-next-forces-capture
- PatternVersion: 1
- Status: active
- Supersedes:

## Task Context
- Triggering task: Performance discovery 2026-09.
- Impacted area: src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs,
  src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs

## Mistake
- What went wrong: The v2 pipeline was designed as "unrolled and allocation-free", but the
  `RequestHandlerDelegate<TResponse>()` signature made per-request closure allocation unavoidable
  in the generated code. The unrolling removed the *dictionary*, not the *allocation*.
- Expected behavior: An unrolled, compile-time pipeline allocates nothing per request.
- Actual behavior: ~88 B per behavior per request — a display class plus one delegate.

## Root Cause Analysis
- Primary cause: A public API shape (copied from MediatR's `RequestHandlerDelegate<TResponse>`)
  dictated a code-generation strategy. The generator had no freedom to avoid the capture.
- Contributing factors:
  - The repo's benchmarks compared only against MediatR, which has the same closure cost, so the
    allocation looked competitive and was never questioned.
  - `[MemoryDiagnoser]` numbers were read as ratios against MediatR rather than against a
    zero-mediator floor, so absolute framework overhead was never isolated.
- Detection gap: No benchmark measured a direct handler call as a baseline, and none compared
  against a library that had solved this.

## Prevention
- When a public delegate or interface shape is copied from another library, check whether that
  shape *forces* an allocation strategy on the implementation before freezing it in a rule.
- Every performance benchmark needs an absolute floor (direct call), not only a competitor ratio.
```

### Docs changes required

| File | Change |
|---|---|
| `docs/pipeline-behaviors.md` | Rewrite every example to the new signature |
| `docs/migration-from-mediatr.md` | `next()` -> `next(request, ct)`; note MediatorLite now diverges from MediatR here and why |
| `docs/observability.md` | Behavior example uses the delegate |
| `README.md` | Behavior example in the feature section |
| `src/MediatorLite.SourceGeneration/README.md` | Emitted-pipeline description |
| `.claude/rules/30-pipeline-behaviors.md` **+ `.agents/` + `.cursor/` mirrors** | Rule 1 currently *pins* the old signature — must be rewritten, all three copies |
| `.claude/commands/add-new-pipeline-behavior.md` + `.claude/instructions/` + mirrors | Template behavior |
| `.claude/skills/mediatorlite-*/SKILL.md` + mirrors | Six skill files reference the delegate |

**Open question for the maintainer:** rule `90` §3 says to extend `docs/migration-v1-to-v2.md`
rather than start a new migration file. For a v2 -> v3 break a new `docs/migration-v2-to-v3.md`
seems more natural, but that contradicts the rule as written. **Your call** — I have not assumed
either way.

### Acceptance criteria

- Three behaviors allocate **0 B** above the `DirectCall` floor in `LargeProject` PipelineBench
  (this requires #4 and the cached fold; do not close on the signature change alone).
- Short-circuit semantics, `[BehaviorOrder]` ordering, and the validation-outermost invariant
  (rule `50` Rule 2) are preserved — existing tests stay green after mechanical migration only.
- Cold start not regressed beyond 5.13 us / 6.85 KB.
- ADR, lesson, and every doc/rule/skill mirror updated in the same PR.
- `MEDL1002` (open-behavior shape) diagnostics still fire correctly against the new shape.

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
