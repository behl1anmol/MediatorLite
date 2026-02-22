---
title: Benchmarks
nav_order: 8
---

# Benchmarks

Performance comparisons between MediatorLite and [MediatR](https://github.com/jbogard/MediatR) across four scenarios: simple request dispatch, single pipeline behavior, multiple pipeline behaviors, and notification publishing.

> Last updated: 2026-02-22 — automated via CI.
{: .note }

## Environment

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.3 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3
  Job-YFEFPZ : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3

IterationCount=10  WarmupCount=3
```

---

## Simple Request

Baseline scenario: a single request dispatched to a single handler with no pipeline behaviors.

| Method                     | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| MediatR_SimpleRequest      | 113.6 ns | 1.36 ns | 0.90 ns |  1.00 | 0.0196 |     328 B |        1.00 |
| MediatorLite_SimpleRequest | 144.8 ns | 0.90 ns | 0.54 ns |  1.27 | 0.0153 |     256 B |        0.78 |

---

## Single Pipeline Behavior

Request dispatched through one open pipeline behavior (simulating a logging or tracing layer).

| Method                    | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| MediatR_WithBehavior      | 212.8 ns | 1.58 ns | 0.94 ns |  1.00 | 0.0381 |     640 B |        1.00 |
| MediatorLite_WithBehavior | 274.7 ns | 1.38 ns | 0.72 ns |  1.29 | 0.0348 |     584 B |        0.91 |

---

## Multiple Pipeline Behaviors

Request dispatched through three behaviors (logging, validation, metrics) — a typical production pipeline.

| Method                             | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|----------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| MediatR_WithMultipleBehaviors      | 369.7 ns | 2.92 ns | 1.74 ns |  1.00 | 0.0639 |    1072 B |        1.00 |
| MediatorLite_WithMultipleBehaviors | 528.5 ns | 3.58 ns | 2.37 ns |  1.43 | 0.0505 |     856 B |        0.80 |

---

## Notifications

A notification published to three handlers, comparing MediatR against MediatorLite's `Sequential` and `Parallel` execution strategies.

| Method                               | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| MediatR_Notification                 | 192.5 ns | 1.50 ns | 0.99 ns |  1.00 | 0.0367 |     616 B |        1.00 |
| MediatorLite_Sequential_Notification | 265.4 ns | 1.76 ns | 1.04 ns |  1.38 | 0.0134 |     224 B |        0.36 |
| MediatorLite_Parallel_Notification   | 321.2 ns | 1.61 ns | 0.96 ns |  1.67 | 0.0205 |     344 B |        0.56 |

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
