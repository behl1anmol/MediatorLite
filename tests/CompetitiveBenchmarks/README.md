# Competitive Benchmarks — MediatorLite vs Mediator vs MediatR

This suite exists to make performance work on MediatorLite **falsifiable**. Every performance
issue in the tracker should cite a number produced here, and every fix should be accepted or
rejected by re-running the relevant project before and after.

It is deliberately separate from `tests/MediatorLite.Benchmarks`:

| | `tests/MediatorLite.Benchmarks` | `tests/CompetitiveBenchmarks` (this suite) |
|---|---|---|
| Compares against | MediatR only | MediatR **and** [martinothamar/Mediator](https://github.com/martinothamar/Mediator) |
| Purpose | Guard the shipped MediatR ratios in `docs/benchmarks.md` | Find and quantify MediatorLite's own bottlenecks |
| Scope | One assembly, one configuration | Four assemblies, because message count and assembly-level attributes change the generated code |

## Why four projects and not one

Two of MediatorLite's most important performance characteristics are **compile-time** properties
of the consuming assembly, so they cannot be varied inside a single benchmark project:

1. **Observability is an assembly-level opt-out.** `[assembly: DisableMediatorLogging]` and
   `[assembly: DisableMediatorTracing]` change what the source generator emits. Measuring the
   default (emitted) and opted-out (not emitted) paths requires two assemblies.
2. **Message count changes the emitted dispatch shape.** MediatorLite emits one type-pattern
   switch arm per request type; martinothamar/Mediator switches from an emitted `switch` to a
   `FrozenDictionary` above 16 messages per kind. A "small project" and a "large project" are
   therefore genuinely different benchmarks, not the same benchmark at a different scale.

| Project | Messages | MediatorLite diagnostics | Answers |
|---|---|---|---|
| `SmallProject` | 1 request type per library | **off** | Best-case per-dispatch overhead vs a direct handler call; cold start (container build + first send) |
| `LargeProject` | 66 request types per library | **off** | Pipeline behavior cost, notification fan-out, per-scope cost, and whether dispatch time depends on a message type's position in the generated switch |
| `DefaultDiagnostics` | 1 request type | **on (default)** | What MediatorLite's *default* configuration costs, against a direct handler call |
| `GeneratorThroughput` | 25 / 100 / 400 handlers | n/a | Build-time cost of `HandlerDiscoveryGenerator` and whether its incremental cache survives an unrelated edit |

## Running

These projects are part of `MediatorLite.sln`, so they open and build in the IDE and with
`dotnet build MediatorLite.sln`, but they are **excluded from CI**.

CI builds and tests `MediatorLite.CI.slnf`, a solution filter that lists every project except this
directory. Nothing about the pipeline changed when the harness was added: `dotnet test` still
discovers exactly one test assembly, and the third-party mediator packages are never restored on a
CI runner.

> **Trade-off, stated plainly:** because CI does not build these projects, a refactor in `src/`
> can break the harness without anyone noticing until someone runs it. If you change a public
> signature in `src/`, build this directory before you push:
> `dotnet build MediatorLite.sln -c Release`.

A `Verify CI solution filter covers every project` step in the build job fails if a project exists
in `MediatorLite.sln` but is absent from the filter and is not under `tests/CompetitiveBenchmarks/`
— so a newly added `src/` project cannot silently escape CI the way this harness deliberately does.

Run them locally:

```bash
# Per-dispatch overhead and cold start, small project
dotnet run -c Release --project tests/CompetitiveBenchmarks/SmallProject -- --filter '*'

# Behaviors, notifications, scoping, and dispatch scaling, 66 message types
dotnet run -c Release --project tests/CompetitiveBenchmarks/LargeProject -- --filter '*'

# Cost of MediatorLite's default (logging + tracing emitted) configuration
dotnet run -c Release --project tests/CompetitiveBenchmarks/DefaultDiagnostics -- --filter '*'

# Source generator throughput and incremental-cache behaviour (plain console app, not BDN)
dotnet run -c Release --project tests/CompetitiveBenchmarks/GeneratorThroughput
```

Narrow a run with `--filter '*Pipeline*'`, or shorten it with
`--warmupCount 3 --iterationCount 5` while iterating.

## Reading the results

Ratios against MediatR are **not** the interesting number here — MediatorLite already beats
MediatR everywhere. Two other comparisons matter:

- **Overhead above `DirectCall`.** `DirectCall` invokes the handler with no mediator at all, so
  `Mean(mediator) - Mean(DirectCall)` is the framework's true cost and
  `Allocated(mediator) - Allocated(DirectCall)` is the allocation the framework adds on top of
  whatever the handler itself allocates.
- **Distance to `Mediator_*`.** That is the target this suite exists to close.

`Mediator_ConcreteClass` measures martinothamar/Mediator's monomorphized `Send(ConcreteRequest)`
overload. MediatorLite has no equivalent — it dispatches only through `IMediator` — so that row
is a capability gap, not an unfair comparison. `Mediator_IMediator` is the like-for-like row.

## Fairness rules

Keep these true when adding scenarios, or the comparison stops meaning anything:

1. **Equal pipeline depth on all three sides.** Same number of behaviors, notification handlers,
   and validators per library.
2. **Behaviors and handlers do no work.** Every fixture returns a constant, so the measurement is
   dispatch overhead, not handler logic.
3. **Null logging everywhere.** All three containers get `NullLoggerFactory` / `NullLogger<>`, so
   no library pays for real log output. (`DefaultDiagnostics` deliberately breaks this to measure
   a realistic logger too — that is its whole point.)
4. **Return the `ValueTask`/`Task` rather than awaiting it** in the benchmark method wherever
   possible, so BenchmarkDotNet's own `async` state machine does not land in the allocation
   column and mask the difference under test.
5. **Every library keeps its own default configuration.** MediatorLite handlers are Transient
   because that is what `AddGeneratedHandlers()` emits; Mediator handlers are Singleton because
   that is its documented default. Changing one side's defaults to flatter the other invalidates
   the comparison. The one exception is
   `MediatorLite_IMediator_SingletonHandler`, which overrides the handler registration on purpose
   to attribute cost between lifetime and dispatch machinery — it is labelled as such.

## Environment caveat

Baseline numbers in `results/` were taken on a shared 4-vCPU cloud VM. Absolute nanosecond values
are not comparable to results from other machines and the run-to-run noise is higher than on bare
metal. **Relative** comparisons within a single run — which is what every conclusion here rests
on — are sound. Always re-baseline on the machine you are measuring a change with, and compare
before/after from the same run.
