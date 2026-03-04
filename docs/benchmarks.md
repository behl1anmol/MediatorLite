---
title: Benchmarks
nav_order: 8
---

# Benchmarks

Performance comparisons between MediatorLite and [MediatR](https://github.com/jbogard/MediatR) across four scenarios: simple request dispatch, single pipeline behavior, multiple pipeline behaviors, and notification publishing.

> Last updated: 2026-03-04 — automated via CI.
{: .note }

## Environment

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.3 LTS (Noble Numbat)
AMD EPYC 7763 2.60GHz, 1 CPU, 4 logical and 2 physical cores
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
| MediatR_SimpleRequest      | 118.8 ns | 1.60 ns | 1.06 ns |  1.00 | 0.0196 |     328 B |        1.00 |
| MediatorLite_SimpleRequest | 149.5 ns | 1.30 ns | 0.77 ns |  1.26 | 0.0153 |     256 B |        0.78 |

---

## Single Pipeline Behavior

Request dispatched through one open pipeline behavior (simulating a logging or tracing layer).

| Method                    | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| MediatR_WithBehavior      | 226.3 ns | 0.49 ns | 0.26 ns |  1.00 | 0.0381 |     640 B |        1.00 |
| MediatorLite_WithBehavior | 300.1 ns | 3.21 ns | 2.13 ns |  1.33 | 0.0348 |     584 B |        0.91 |

---

## Multiple Pipeline Behaviors

Request dispatched through three behaviors (logging, validation, metrics) — a typical production pipeline.

| Method                             | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| MediatR_WithMultipleBehaviors      | 384.4 ns | 4.99 ns | 2.97 ns |  1.00 |    0.01 | 0.0639 |    1072 B |        1.00 |
| MediatorLite_WithMultipleBehaviors | 573.6 ns | 7.71 ns | 5.10 ns |  1.49 |    0.02 | 0.0505 |     856 B |        0.80 |

---

## Notifications

A notification published to three handlers, comparing MediatR against MediatorLite's `Sequential` and `Parallel` execution strategies.

| Method                               | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| MediatR_Notification                 | 211.6 ns | 2.40 ns | 1.59 ns |  1.00 | 0.0367 |     616 B |        1.00 |
| MediatorLite_Sequential_Notification | 267.5 ns | 1.56 ns | 1.03 ns |  1.26 | 0.0134 |     224 B |        0.36 |
| MediatorLite_Parallel_Notification   | 346.3 ns | 1.06 ns | 0.70 ns |  1.64 | 0.0176 |     296 B |        0.48 |

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
