---
title: Benchmarks
nav_order: 8
---

# Benchmarks

Performance comparisons between MediatorLite and [MediatR](https://github.com/jbogard/MediatR) across four scenarios: simple request dispatch, single pipeline behavior, multiple pipeline behaviors, and notification publishing.

> Last updated: 2026-06-10 — v2 typed-switch dispatch architecture (ValueTask end-to-end).
{: .note }

## Environment

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4
  Job-YFEFPZ : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4

IterationCount=10  WarmupCount=3
```

---

## Simple Request

Baseline scenario: a single request dispatched to a single handler with no pipeline behaviors.

| Method                     | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| MediatR_SimpleRequest      | 113.07 ns | 2.136 ns | 1.413 ns |  1.00 |    0.02 | 0.0038 |     328 B |        1.00 |
| MediatorLite_SimpleRequest |  56.21 ns | 1.419 ns | 0.939 ns |  0.50 |    0.01 | 0.0018 |     152 B |        0.46 |

---

## Single Pipeline Behavior

Request dispatched through one pipeline behavior (simulating a logging or tracing layer).

| Method                    | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| MediatR_WithBehavior      | 229.9 ns | 5.07 ns | 3.35 ns |  1.00 | 0.0076 |     640 B |        1.00 |
| MediatorLite_WithBehavior | 107.1 ns | 1.76 ns | 1.17 ns |  0.47 | 0.0033 |     280 B |        0.44 |

---

## Multiple Pipeline Behaviors

Request dispatched through three behaviors (logging, validation, metrics) — a typical production pipeline.

| Method                             | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|----------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| MediatR_WithMultipleBehaviors      | 368.0 ns | 7.35 ns | 4.86 ns |  1.00 | 0.0124 |    1072 B |        1.00 |
| MediatorLite_WithMultipleBehaviors | 183.5 ns | 3.27 ns | 1.71 ns |  0.50 | 0.0057 |     488 B |        0.46 |

---

## Notifications

A notification published to three handlers, comparing MediatR against MediatorLite's `Sequential` and `Parallel` execution strategies.

| Method                               | Mean      | Error    | StdDev   | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------------------- |----------:|---------:|---------:|------:|-------:|----------:|------------:|
| MediatR_Notification                 | 228.37 ns | 4.760 ns | 3.148 ns |  1.00 | 0.0072 |     616 B |        1.00 |
| MediatorLite_Sequential_Notification |  72.23 ns | 2.201 ns | 1.456 ns |  0.32 | 0.0011 |      96 B |        0.16 |
| MediatorLite_Parallel_Notification   |  69.69 ns | 0.613 ns | 0.365 ns |  0.31 | 0.0011 |      96 B |        0.16 |

---

## Key Takeaways

MediatorLite beats MediatR on **both latency and allocations in every scenario** — roughly 2x faster on request dispatch (at half the allocations) and 3x faster on notification publishing (at one-sixth the allocations). The v2 architecture achieves this by generating an `IMediator` implementation whose dispatch is a compile-time type-pattern switch with fully typed `ValueTask` pipelines: no dictionary lookup, no `Task<object>` boxing, no runtime wrapper, and no async state machine on the zero-behavior path.

> Benchmark fairness note: each MediatorLite scenario uses its own request type with *closed* pipeline behaviors (0/1/3) so the behavior count exactly matches the corresponding MediatR setup. Open-generic behaviors would otherwise be applied to every request type in the assembly at compile time.

---

## Running Benchmarks Locally

```bash
cd tests/MediatorLite.Benchmarks
dotnet run --configuration Release -- --filter '*' --exporters json markdown --memory
```

Results are written to `BenchmarkDotNet.Artifacts/results/`.

## REST API Benchmarks (Production-Like)

The repository also includes a REST API benchmark project at `tests/MediatorLite.RestApiBenchmarks`.
This suite compares MediatorLite and MediatR in end-to-end API scenarios backed by SQLite:

- In-process transport (`TestServer`) and real localhost transport (`Kestrel`)
- Read-heavy and write-heavy API operations
- Concurrency scenarios with multiple in-flight requests

Run read/write benchmarks:

```bash
dotnet run -c Release --project tests/MediatorLite.RestApiBenchmarks -- --filter '*RestApiReadWriteBenchmarks*'
```

Run concurrency benchmarks:

```bash
dotnet run -c Release --project tests/MediatorLite.RestApiBenchmarks -- --filter '*RestApiConcurrencyBenchmarks*'
```
