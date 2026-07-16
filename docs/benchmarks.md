---
title: Benchmarks
nav_order: 8
---

# Benchmarks

Performance comparisons between MediatorLite and [MediatR](https://github.com/jbogard/MediatR) across four scenarios: simple request dispatch, single pipeline behavior, multiple pipeline behaviors, and notification publishing.

> Last updated: 2026-07-16 — automated via CI.
{: .note }

## Environment

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  Job-YFEFPZ : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

IterationCount=10  WarmupCount=3
```

---

## Simple Request

Baseline scenario: a single request dispatched to a single handler with no pipeline behaviors.

| Method                     | Mean      | Error    | StdDev   | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |----------:|---------:|---------:|------:|-------:|----------:|------------:|
| MediatR_SimpleRequest      | 113.62 ns | 1.421 ns | 0.940 ns |  1.00 | 0.0196 |     328 B |        1.00 |
| MediatorLite_SimpleRequest |  58.74 ns | 1.016 ns | 0.672 ns |  0.52 | 0.0091 |     152 B |        0.46 |

---

## Single Pipeline Behavior

Request dispatched through one open pipeline behavior (simulating a logging or tracing layer).

| Method                    | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| MediatR_WithBehavior      | 220.2 ns | 7.16 ns | 4.74 ns |  1.00 |    0.03 | 0.0381 |     640 B |        1.00 |
| MediatorLite_WithBehavior | 120.2 ns | 0.96 ns | 0.57 ns |  0.55 |    0.01 | 0.0167 |     280 B |        0.44 |

---

## Multiple Pipeline Behaviors

Request dispatched through three behaviors (logging, validation, metrics) — a typical production pipeline.

| Method                             | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|----------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| MediatR_WithMultipleBehaviors      | 345.3 ns | 5.26 ns | 3.13 ns |  1.00 | 0.0639 |    1072 B |        1.00 |
| MediatorLite_WithMultipleBehaviors | 279.6 ns | 2.88 ns | 1.90 ns |  0.81 | 0.0291 |     488 B |        0.46 |

---

## Notifications

A notification published to three handlers, comparing MediatR against MediatorLite's `Sequential` and `Parallel` execution strategies.

| Method                               | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| MediatR_Notification                 | 199.3 ns | 3.98 ns | 2.63 ns |  1.00 |    0.02 | 0.0367 |     616 B |        1.00 |
| MediatR_Parallel_Notification        | 309.0 ns | 3.60 ns | 2.38 ns |  1.55 |    0.02 | 0.0582 |     976 B |        1.58 |
| MediatorLite_Sequential_Notification | 136.5 ns | 0.48 ns | 0.31 ns |  0.68 |    0.01 | 0.0057 |      96 B |        0.16 |
| MediatorLite_Parallel_Notification   | 141.3 ns | 0.43 ns | 0.29 ns |  0.71 |    0.01 | 0.0057 |      96 B |        0.16 |

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
