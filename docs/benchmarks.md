---
title: Benchmarks
nav_order: 8
---

# Benchmarks

Performance comparisons between MediatorLite and [MediatR](https://github.com/jbogard/MediatR) across four scenarios: simple request dispatch, single pipeline behavior, multiple pipeline behaviors, and notification publishing.

> Last updated: 2026-07-11 — automated via CI.
{: .note }

## Environment

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.24GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  Job-YFEFPZ : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

IterationCount=10  WarmupCount=3
```

---

## Simple Request

Baseline scenario: a single request dispatched to a single handler with no pipeline behaviors.

| Method                     | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| MediatR_SimpleRequest      | 115.05 ns | 2.855 ns | 1.888 ns |  1.00 |    0.02 | 0.0196 |     328 B |        1.00 |
| MediatorLite_SimpleRequest |  55.73 ns | 1.032 ns | 0.614 ns |  0.48 |    0.01 | 0.0091 |     152 B |        0.46 |

---

## Single Pipeline Behavior

Request dispatched through one open pipeline behavior (simulating a logging or tracing layer).

| Method                    | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| MediatR_WithBehavior      | 221.2 ns | 4.34 ns | 2.87 ns |  1.00 |    0.02 | 0.0381 |     640 B |        1.00 |
| MediatorLite_WithBehavior | 104.1 ns | 2.10 ns | 1.39 ns |  0.47 |    0.01 | 0.0167 |     280 B |        0.44 |

---

## Multiple Pipeline Behaviors

Request dispatched through three behaviors (logging, validation, metrics) — a typical production pipeline.

| Method                             | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|----------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| MediatR_WithMultipleBehaviors      | 348.6 ns | 4.84 ns | 3.20 ns |  1.00 | 0.0639 |    1072 B |        1.00 |
| MediatorLite_WithMultipleBehaviors | 266.8 ns | 1.90 ns | 1.13 ns |  0.77 | 0.0291 |     488 B |        0.46 |

---

## Notifications

A notification published to three handlers, comparing MediatR against MediatorLite's `Sequential` and `Parallel` execution strategies.

| Method                               | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| MediatR_Notification                 | 205.4 ns | 5.39 ns | 3.57 ns |  1.00 |    0.02 | 0.0367 |     616 B |        1.00 |
| MediatR_Parallel_Notification        | 297.5 ns | 4.86 ns | 3.22 ns |  1.45 |    0.03 | 0.0582 |     976 B |        1.58 |
| MediatorLite_Sequential_Notification | 138.9 ns | 0.41 ns | 0.24 ns |  0.68 |    0.01 | 0.0057 |      96 B |        0.16 |
| MediatorLite_Parallel_Notification   | 135.8 ns | 0.40 ns | 0.24 ns |  0.66 |    0.01 | 0.0057 |      96 B |        0.16 |

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
