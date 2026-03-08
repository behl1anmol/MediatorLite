---
name: mediatorlite-benchmarks
description: >
  Knowledge skill for the MediatorLite.Benchmarks project comparing MediatorLite against MediatR
  using BenchmarkDotNet. Use this skill whenever writing new benchmarks, interpreting benchmark results,
  adding comparison scenarios, understanding MediatorLite vs MediatR performance tradeoffs, or optimizing
  MediatorLite performance. Also use when the user asks about performance characteristics, allocation
  patterns, or throughput comparisons. Even if the user just mentions "benchmark", "performance",
  "allocations", "MediatR comparison", "latency", or "throughput", use this skill.
---

# MediatorLite Benchmarks

## Project Overview

The benchmark project lives in `tests/MediatorLite.Benchmarks/`. It compares MediatorLite (source-generated dispatch) against MediatR across multiple scenarios using BenchmarkDotNet.

| Dependency | Version | Purpose |
|---|---|---|
| BenchmarkDotNet | 0.15.8 | Benchmark framework with statistical analysis |
| MediatR | 12.2.0 | Baseline mediator library for comparison |
| Microsoft.Extensions.DependencyInjection | 9.0.0 | DI container for both mediators |
| Microsoft.Extensions.Logging | 9.0.0 | Required by MediatorLite (replaced with NullLoggerFactory) |

Everything is in a single file: `tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs`. The project references the MediatorLite source generator as an analyzer and the core MediatorLite library. All benchmark types (requests, handlers, behaviors, notifications) are defined inline as nested classes within the benchmark classes.

## Benchmark Classes

Four benchmark classes cover the key dispatch scenarios:

### 1. MediatorBenchmarks — Simple Request

Baseline scenario: one request → one handler, no pipeline behaviors.

- **MediatR_SimpleRequest** (baseline): `await _mediatr.Send(new MediatRQuery(1))`
- **MediatorLite_SimpleRequest**: `await _mediatorLite.SendAsync(new MediatorLiteQuery(1))`

Isolates raw dispatcher overhead without any pipeline wrapping.

### 2. PipelineBenchmarks — Single Behavior

One open generic pipeline behavior (simulated logging) wrapping the handler.

- MediatorLite registers `MediatorLiteLoggingBehavior<,>` via `services.AddTransient(typeof(IPipelineBehavior<,>), ...)`
- MediatR registers `MediatRLoggingBehavior<,>` via `cfg.AddOpenBehavior(...)`

### 3. MultipleBehaviorsBenchmarks — Three Behaviors

A realistic production pipeline with three open generic behaviors: logging, validation, and metrics. All behaviors are no-op (just `await next()`) to isolate pipeline construction cost.

- MediatorLite: registers three `IPipelineBehavior<,>` implementations
- MediatR: registers three via `cfg.AddOpenBehavior()`

### 4. NotificationBenchmarks — Three Handlers

A notification published to three handlers, comparing execution strategies:

- **MediatR_Notification** (baseline): default sequential execution
- **MediatorLite_Sequential_Notification**: `NotificationExecutionStrategy.Sequential`
- **MediatorLite_Parallel_Notification**: `NotificationExecutionStrategy.Parallel`

Two separate `ServiceProvider` instances are built for the sequential and parallel MediatorLite configurations.

## Methodology

All classes use identical BenchmarkDotNet configuration:

```csharp
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
```

Key design decisions:
- **Zero-work handlers**: All handlers return immediately (`ValueTask.FromResult` or `ValueTask.CompletedTask`). This isolates dispatcher/pipeline overhead from application logic.
- **NullLoggerFactory**: Eliminates logging overhead. Both `ILoggerFactory` and `ILogger<>` are registered as `NullLoggerFactory`/`NullLogger<>`.
- **Source-gen dispatch**: MediatorLite always uses `ISourceGeneratedMediator` (the `SourceGeneratedMediator` class from `MediatorLite.Generated`). Built-in logging and tracing are disabled.
- **ValueTask vs Task**: MediatorLite handlers return `ValueTask`; MediatR handlers return `Task`. This difference contributes to MediatorLite's lower allocation profile.
- **MediatR baseline**: MediatR is always the `[Benchmark(Baseline = true)]` method in each class.

## Results Summary

| Scenario | MediatorLite Time Ratio | MediatorLite Alloc Ratio | MediatorLite Allocated | MediatR Allocated |
|---|---|---|---|---|
| Simple Request | 1.26x slower | 0.78x (22% less) | 256 B | 328 B |
| Single Behavior | 1.33x slower | 0.91x (9% less) | 584 B | 640 B |
| Multiple Behaviors (3) | 1.49x slower | 0.80x (20% less) | 856 B | 1,072 B |
| Notification Sequential | 1.26x slower | 0.36x (64% less) | 224 B | 616 B |
| Notification Parallel | 1.64x slower | 0.48x (52% less) | 296 B | 616 B |

## Key Findings

1. **Lower allocations across all scenarios** — MediatorLite allocates 22–64% less memory than MediatR in every benchmark. The `ValueTask`-based pipeline and source-generated dispatch avoid `Task` state machine allocations.

2. **Sequential notifications are the strongest scenario** — Near-identical latency to MediatR (1.26x) but less than half the allocations (224 B vs 616 B). This is the recommended strategy for most use cases.

3. **Throughput gap narrows with more behaviors** — The allocation savings scale as more behaviors are added (from 72 B savings with 1 behavior to 216 B with 3), while the time ratio only increases modestly (1.33x → 1.49x).

4. **Parallel notification overhead** — For trivial zero-work handlers, parallel mode adds overhead (1.64x) and extra allocations (296 B). In real-world scenarios with I/O-bound handlers, this overhead disappears and parallelism provides genuine throughput gains.

5. **Raw throughput vs allocation profile** — MediatorLite trades raw per-call speed for a predictable, low-allocation footprint. In high-throughput scenarios where GC pressure matters, the allocation savings can outweigh the latency difference.

## Running Benchmarks

```bash
cd tests/MediatorLite.Benchmarks
dotnet run --configuration Release -- --filter '*' --exporters json markdown --memory
```

Results are written to `BenchmarkDotNet.Artifacts/results/`.

To run a specific benchmark class:

```bash
dotnet run --configuration Release -- --filter '*MediatorBenchmarks*'
dotnet run --configuration Release -- --filter '*PipelineBenchmarks*'
dotnet run --configuration Release -- --filter '*NotificationBenchmarks*'
dotnet run --configuration Release -- --filter '*MultipleBehaviorsBenchmarks*'
```

## References

For detailed methodology and results analysis, read these reference files:

- [references/methodology.md](references/methodology.md) — Benchmark configuration, handler design, setup pattern, ValueTask vs Task impact, how to run locally
- [references/results-analysis.md](references/results-analysis.md) — Full results tables, strengths and weaknesses, interpretation guidance, BenchmarkDotNet output columns
