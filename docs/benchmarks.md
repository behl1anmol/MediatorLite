---
title: Benchmarks
nav_order: 8
---

# Benchmarks

Performance comparisons between MediatorLite and [MediatR](https://github.com/jbogard/MediatR) across four scenarios: simple request dispatch, single pipeline behavior, multiple pipeline behaviors, and notification publishing.

> Last updated: 2026-06-10 — automated via CI.
{: .note }

## Environment

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  Job-YFEFPZ : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

IterationCount=10  WarmupCount=3
```

---

## Simple Request

Baseline scenario: a single request dispatched to a single handler with no pipeline behaviors.

| Method                     | Mean      | Error    | StdDev   | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |----------:|---------:|---------:|------:|-------:|----------:|------------:|
| MediatR_SimpleRequest      | 113.34 ns | 0.802 ns | 0.420 ns |  1.00 | 0.0196 |     328 B |        1.00 |
| MediatorLite_SimpleRequest |  54.50 ns | 0.536 ns | 0.355 ns |  0.48 | 0.0091 |     152 B |        0.46 |

---

## Single Pipeline Behavior

Request dispatched through one open pipeline behavior (simulating a logging or tracing layer).

| Method                    | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| MediatR_WithBehavior      | 210.9 ns | 4.07 ns | 2.69 ns |  1.00 |    0.02 | 0.0381 |     640 B |        1.00 |
| MediatorLite_WithBehavior | 108.3 ns | 0.93 ns | 0.55 ns |  0.51 |    0.01 | 0.0167 |     280 B |        0.44 |

---

## Multiple Pipeline Behaviors

Request dispatched through three behaviors (logging, validation, metrics) — a typical production pipeline.

| Method                             | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|----------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| MediatR_WithMultipleBehaviors      | 350.1 ns | 1.27 ns | 0.75 ns |  1.00 | 0.0639 |    1072 B |        1.00 |
| MediatorLite_WithMultipleBehaviors | 269.4 ns | 1.75 ns | 1.16 ns |  0.77 | 0.0291 |     488 B |        0.46 |

---

## Notifications

A notification published to three handlers, comparing MediatR against MediatorLite's `Sequential` and `Parallel` execution strategies.

| Method                               | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| MediatR_Notification                 | 217.4 ns | 0.89 ns | 0.53 ns |  1.00 | 0.0367 |     616 B |        1.00 |
| MediatorLite_Sequential_Notification | 136.4 ns | 0.26 ns | 0.14 ns |  0.63 | 0.0057 |      96 B |        0.16 |
| MediatorLite_Parallel_Notification   | 136.4 ns | 0.56 ns | 0.37 ns |  0.63 | 0.0057 |      96 B |        0.16 |

---

## Key Takeaways

MediatorLite consistently allocates less memory across every scenario. The notification sequential mode is especially notable — near-identical latency to MediatR but less than half the allocations. The `ValueTask`-based pipeline and source-generated dispatch path trade raw per-call speed for a predictable, low-allocation footprint.

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
