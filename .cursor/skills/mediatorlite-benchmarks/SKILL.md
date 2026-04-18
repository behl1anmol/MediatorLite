---
name: mediatorlite-benchmarks
description: Reference for the MediatorLite.Benchmarks project -- BenchmarkDotNet setup (MemoryDiagnoser, SimpleJob warmupCount=3 iterationCount=10), four benchmark classes (MediatorBenchmarks / PipelineBenchmarks / MultipleBehaviorsBenchmarks / NotificationBenchmarks), MediatorLite vs MediatR comparison methodology, AssemblyInfo.cs observability opt-out, how to run, and docs/benchmarks.md result interpretation.
triggers: BenchmarkDotNet, MediatorBenchmarks, MediatR comparison, memory diagnoser, throughput, mediator benchmarks, PipelineBenchmarks, MultipleBehaviorsBenchmarks, NotificationBenchmarks, benchmark comparison, ValueTask vs Task, boxing benchmark, docs/benchmarks.md
---

# MediatorLite.Benchmarks

## Purpose

`MediatorLite.Benchmarks` is a BenchmarkDotNet console project that produces apples-to-apples performance comparisons between MediatorLite and [MediatR 12.2.0](https://github.com/jbogard/MediatR) across four scenarios: simple request dispatch, single pipeline behavior, multiple pipeline behaviors, and notification publishing (sequential + parallel). Each benchmark class sets up **two** DI containers — one for each library — and invokes equivalent types. Observability is disabled at assembly level so the numbers reflect pure dispatch cost, not logging/tracing overhead.

## When to use

- Measuring the cost of a dispatch change (e.g. altering the generator's emitted pipeline).
- Validating a perf regression claim before/after a code change.
- Adding a new benchmark scenario (e.g. benchmarking streaming, open generic behaviors at scale, boxing value-type responses).
- Debugging memory allocations — `[MemoryDiagnoser]` reports `Gen0/Gen1/Gen2` + `Allocated` + `Alloc Ratio`.
- Updating [docs/benchmarks.md](docs/benchmarks.md) with fresh results.

## Project location & entry points

- [MediatorLite.Benchmarks.csproj](tests/MediatorLite.Benchmarks/MediatorLite.Benchmarks.csproj) — `OutputType=Exe`, references `BenchmarkDotNet 0.15.8`, `MediatR 12.2.0`, `MediatorLite.Abstractions` + `MediatorLite` via project refs, and the source generator as `OutputItemType="Analyzer"`.
- [MediatorBenchmarks.cs](tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs) — top-level `BenchmarkRunner.Run<>` calls and all four benchmark classes + shared types.
- [AssemblyInfo.cs](tests/MediatorLite.Benchmarks/AssemblyInfo.cs) — the observability kill-switch.
- [docs/benchmarks.md](docs/benchmarks.md) — published results (updated via CI).

## Core types / API surface

### Entry point — top-level `BenchmarkRunner.Run<>()` calls

```9:12:tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs
BenchmarkRunner.Run<MediatorBenchmarks>();
BenchmarkRunner.Run<PipelineBenchmarks>();
BenchmarkRunner.Run<NotificationBenchmarks>();
BenchmarkRunner.Run<MultipleBehaviorsBenchmarks>();
```

All four classes are decorated identically:

```14:16:tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MediatorBenchmarks
```

### Observability opt-out

```1:4:tests/MediatorLite.Benchmarks/AssemblyInfo.cs
using MediatorLite;

[assembly: DisableMediatorLogging]
[assembly: DisableMediatorTracing]
```

The generator reads these assembly-level attributes and emits the fast path **without** `try/catch`, `ILogger` calls, or `ActivitySource.StartActivity` calls. This is mandatory for benchmarks — including diagnostics would skew results and introduce `ActivitySource`-dependent variance.

### `MediatorBenchmarks` — simple request, no behaviors

Parallel MediatorLite + MediatR type definitions keep the test fair:

```25:34:tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs
    public record MediatorLiteQuery(int Id) : MediatorLite.IRequest<MediatorLiteResult>;
    public record MediatorLiteResult(int Id, string Name);

    public class MediatorLiteHandler : MediatorLite.IRequestHandler<MediatorLiteQuery, MediatorLiteResult>
    {
        public ValueTask<MediatorLiteResult> HandleAsync(MediatorLiteQuery request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new MediatorLiteResult(request.Id, "Test"));
        }
    }
```

The `[GlobalSetup]` builds two service providers side by side:

```209:230:tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs
    [GlobalSetup]
    public void Setup()
    {
        // Setup MediatorLite with v2 source-gen dispatch
        // AddGeneratedHandlers() auto-registers all discovered handlers, behaviors, and SourceGeneratedMediator
        var mediatorLiteServices = new ServiceCollection();
        mediatorLiteServices.AddGeneratedHandlers();
        mediatorLiteServices.AddMediatorLite();
        mediatorLiteServices.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        mediatorLiteServices.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _mediatorLiteProvider = mediatorLiteServices.BuildServiceProvider();
        _mediatorLite = _mediatorLiteProvider.GetRequiredService<MediatorLite.IMediator>();

        // Setup MediatR
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<MediatorBenchmarks>();
        });
        _mediatrProvider = mediatrServices.BuildServiceProvider();
        _mediatr = _mediatrProvider.GetRequiredService<MediatR.IMediator>();
    }
```

`NullLoggerFactory` is registered to satisfy DI even though logging is compile-time disabled (any stray `ILogger` resolution still needs a factory). The MediatR side uses the library's standard assembly scanner.

`MediatR_SimpleRequest` is the **baseline**:

```232:242:tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs
    [Benchmark(Baseline = true)]
    public async Task<MediatRResult> MediatR_SimpleRequest()
    {
        return await _mediatr.Send(new MediatRQuery(1));
    }

    [Benchmark]
    public async Task<MediatorLiteResult> MediatorLite_SimpleRequest()
    {
        return await _mediatorLite.SendAsync(new MediatorLiteQuery(1));
    }
```

### `PipelineBenchmarks` — one behavior

The MediatorLite side relies on generator auto-registration of `MediatorLiteLoggingBehavior<,>`; the MediatR side must explicitly add it:

```274:283:tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs
        // Setup MediatR with behaviors
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<MediatorBenchmarks>();
            cfg.AddOpenBehavior(typeof(MediatorBenchmarks.MediatRLoggingBehavior<,>));
        });
        _mediatrProvider = mediatrServices.BuildServiceProvider();
        _mediatr = _mediatrProvider.GetRequiredService<MediatR.IMediator>();
    }
```

The three open generic behaviors under test (logging / validation / metrics) are trivial pass-through implementations — each simply `await next()`:

```36:46:tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs
    public class MediatorLiteLoggingBehavior<TRequest, TResponse> : MediatorLite.IPipelineBehavior<TRequest, TResponse>
        where TRequest : MediatorLite.IRequest<TResponse>
    {
        public async ValueTask<TResponse> HandleAsync(
            TRequest request,
            MediatorLite.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken = default)
        {
            return await next();
        }
    }
```

### `MultipleBehaviorsBenchmarks` — three behaviors (logging + validation + metrics)

The three stacked behaviors simulate a realistic production stack. For MediatorLite, the generator auto-discovers and orders them; for MediatR they are added via three `cfg.AddOpenBehavior(...)` calls.

### `NotificationBenchmarks` — Sequential vs Parallel publish

Two notification records differ only by attribute:

```74:103:tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs
    // Notification types - Sequential (library default, no attribute)
    public record MediatorLiteNotification(int Id) : MediatorLite.INotification;

    public class MediatorLiteNotificationHandler1 : MediatorLite.INotificationHandler<MediatorLiteNotification>
    {
        public ValueTask HandleAsync(MediatorLiteNotification notification, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }
```

```101:104:tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs
    // Notification types - Parallel (compile-time attribute)
    [MediatorLite.NotificationExecution(MediatorLite.NotificationExecutionStrategy.Parallel)]
    public record MediatorLiteNotificationParallel(int Id) : MediatorLite.INotification;
```

The emission difference is resolved at compile time — no runtime branching, so the benchmark measures the true cost of the chosen strategy.

## Patterns & invariants

**Do:**
- Keep `[MemoryDiagnoser]` on every benchmark class so allocation counts are reported.
- Keep `SimpleJob(warmupCount: 3, iterationCount: 10)` for reproducibility with [docs/benchmarks.md](docs/benchmarks.md).
- Keep MediatR as `[Benchmark(Baseline = true)]` so BenchmarkDotNet computes `Ratio` against it.
- Use `NullLoggerFactory` + `NullLogger<>` in both containers to avoid I/O noise.
- Use `ValueTask.CompletedTask` / `ValueTask.FromResult(...)` in handler bodies — the point is to measure framework dispatch, not user code.

**Don't:**
- Don't remove the `[assembly: DisableMediatorLogging]` / `[assembly: DisableMediatorTracing]` from `AssemblyInfo.cs` — it is essential to match MediatR's zero-observability baseline.
- Don't run benchmarks in `Debug` configuration — BenchmarkDotNet will refuse.
- Don't add async work with `Task.Delay` to handler bodies; it hides the dispatch overhead you're trying to measure.
- Don't mix MediatorLite and MediatR types in the same container — they have separate DI trees (`_mediatorLiteServices` vs `_mediatrServices`).

## Common tasks

1. **Run all benchmarks**
   ```
   dotnet run -c Release --project tests/MediatorLite.Benchmarks
   ```
   BenchmarkDotNet emits console tables and writes artifacts under `BenchmarkDotNet.Artifacts/`.

2. **Run a single benchmark class**
   - Edit `Program.cs` entry (top-level `BenchmarkRunner.Run<>()` calls) to comment out the ones you don't need, or pass a filter:
   ```
   dotnet run -c Release --project tests/MediatorLite.Benchmarks -- --filter '*MediatorBenchmarks*'
   ```

3. **Add a new benchmark scenario**
   1. Add a public class with `[MemoryDiagnoser]` + `[SimpleJob(warmupCount: 3, iterationCount: 10)]`.
   2. Define parallel MediatorLite/MediatR types.
   3. Set up two providers in `[GlobalSetup]`; register generator auto-discovery for MediatorLite and explicit handlers/behaviors for MediatR.
   4. Add a `BenchmarkRunner.Run<YourClass>()` line at the top.
   5. Mark `[Benchmark(Baseline = true)]` on the MediatR method for ratio computation.

4. **Update the published results in `docs/benchmarks.md`**
   - Run `dotnet run -c Release --project tests/MediatorLite.Benchmarks`.
   - Copy the BenchmarkDotNet-formatted tables into the relevant section of [docs/benchmarks.md](docs/benchmarks.md).
   - Update the header timestamp (e.g. `> Last updated: <date>`).

5. **Interpret results (`docs/benchmarks.md` conventions)**
   - `Mean` — average time per operation.
   - `Ratio` — mean relative to MediatR baseline (`1.00`). `1.24` means MediatorLite is 24% slower in that metric.
   - `Alloc Ratio` — allocation relative to baseline. `0.78` means MediatorLite allocates 22% less.
   - `Gen0/Gen1/Gen2` — GC events per 1000 ops. Lower is better.

## Pitfalls & gotchas

- **Boxing on value-type responses**: `RequestDispatcher` returns `Task<object>`, so a response like `int` or `Guid` incurs a heap box per call. See the note in [ISourceGeneratedMediator.cs](src/MediatorLite.Abstractions/Abstractions/ISourceGeneratedMediator.cs). Current benchmarks use reference-type responses (records), so the box is amortized; keep this in mind when adding new scenarios.
- **`[assembly: Disable*]` is project-wide**: even if you test "with logging enabled" later, you must remove those attributes — no runtime toggle exists.
- **MediatR's `Send` returns `Task<T>`, MediatorLite's `SendAsync` returns `Task<T>`** at the public API layer — but MediatorLite's handlers internally use `ValueTask<T>`. The benchmark intentionally compares the outer `Task<T>` path for parity.
- **`warmupCount: 3, iterationCount: 10`** is a trade-off for CI speed. For higher-confidence results, increase `iterationCount` or use `[SimpleJob]` with BenchmarkDotNet's default `Default` job.
- **MediatR 12.x is the reference version**. If you bump MediatR in [MediatorLite.Benchmarks.csproj](tests/MediatorLite.Benchmarks/MediatorLite.Benchmarks.csproj), update [docs/benchmarks.md](docs/benchmarks.md) accordingly — different MediatR versions have different allocation profiles.
- **`NotificationBenchmarks`** uses the library default (`Sequential` + `StopOnFirstError`) for the un-attributed notification; results reflect exactly one code path per strategy because the generator emits no runtime branch on strategy.

## Related skills & rules

- **mediatorlite-abstractions** — `[DisableMediatorLogging]` / `[DisableMediatorTracing]` / `[NotificationExecution]` used here live in `Attributes.cs`.
- **mediatorlite-core** — `AddMediatorLite()` runtime under test.
- **mediatorlite-source-generation** — the generator whose emitted dispatch is the thing being measured.
- **mediatorlite-rest-api-benchmarks** — ASP.NET Core end-to-end benchmarks with EF Core and MediatR parity harness; use when you need real-world scenario numbers instead of microbenchmarks.
- Docs: [docs/benchmarks.md](docs/benchmarks.md) (published results), [docs/observability.md](docs/observability.md) (why opt-out matters).
