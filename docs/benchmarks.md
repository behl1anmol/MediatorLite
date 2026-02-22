---
title: Benchmarks
nav_order: 8
---

# Benchmarks

Performance comparisons between MediatorLite and [MediatR](https://github.com/jbogard/MediatR) across four scenarios: simple request dispatch, single pipeline behavior, multiple pipeline behaviors, and notification publishing.

> This page is automatically updated by CI after every benchmark run on `main`.
{: .note }

## Environment

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.28020.1362)
Intel Core i5-7200U CPU 2.50GHz (Kaby Lake), 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.102 — .NET 10.0.2 (X64 RyuJIT AVX2)
IterationCount=10  WarmupCount=3  MemoryDiagnoser=true
```

---

## Simple Request

Baseline scenario: a single request dispatched to a single handler with no pipeline behaviors.

| Method | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---|--:|--:|--:|--:|--:|--:|--:|
| MediatR_SimpleRequest | 152.5 ns | 8.27 ns | 4.33 ns | 1.00 | 0.2089 | 328 B | 1.00 |
| MediatorLite_SimpleRequest | 246.9 ns | 44.95 ns | 29.73 ns | 1.62 | 0.1631 | 256 B | 0.78 |

**22% less memory** (256 B vs 328 B).

---

## Single Pipeline Behavior

Request dispatched through one open pipeline behavior (simulating a logging or tracing layer).

| Method | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---|--:|--:|--:|--:|--:|--:|--:|
| MediatR_WithBehavior | 337.9 ns | 62.73 ns | 41.49 ns | 1.00 | 0.4077 | 640 B | 1.00 |
| MediatorLite_WithBehavior | 687.8 ns | 220.68 ns | 145.97 ns | 2.06 | 0.3719 | 584 B | 0.91 |

**9% less memory** (584 B vs 640 B).

---

## Multiple Pipeline Behaviors

Request dispatched through three behaviors (logging, validation, metrics) — a typical production pipeline.

| Method | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---|--:|--:|--:|--:|--:|--:|--:|
| MediatR_WithMultipleBehaviors | 477.9 ns | 32.34 ns | 16.91 ns | 1.00 | 0.6828 | 1072 B | 1.00 |
| MediatorLite_WithMultipleBehaviors | 793.6 ns | 124.19 ns | 82.15 ns | 1.66 | 0.5455 | 856 B | 0.80 |

**20% less memory** (856 B vs 1072 B).

---

## Notifications

A notification published to three handlers, comparing MediatR against MediatorLite's `Sequential` and `Parallel` execution strategies.

| Method | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---|--:|--:|--:|--:|--:|--:|--:|
| MediatR_Notification | 319.4 ns | 51.82 ns | 34.27 ns | 1.00 | 0.3920 | 616 B | 1.00 |
| MediatorLite_Sequential_Notification | 343.6 ns | 82.72 ns | 49.23 ns | 1.09 | 0.1426 | 224 B | 0.36 |
| MediatorLite_Parallel_Notification | 582.0 ns | 144.16 ns | 95.36 ns | 1.84 | 0.2193 | 344 B | 0.56 |

**Sequential: 64% less memory** (224 B vs 616 B) at only 9% higher latency.
**Parallel: 44% less memory** (344 B vs 616 B) with concurrent execution.

---

## Key Takeaways

| Scenario | Latency vs MediatR | Memory vs MediatR |
|---|---|---|
| Simple request | 1.62× | **−22%** |
| Single behavior | 2.06× | **−9%** |
| Multiple behaviors | 1.66× | **−20%** |
| Notifications (sequential) | 1.09× | **−64%** |
| Notifications (parallel) | 1.84× | **−44%** |

MediatorLite consistently allocates less memory across every scenario. The notification sequential mode is especially notable — virtually the same latency as MediatR but less than half the allocations. The higher latency numbers in other scenarios reflect the `ValueTask`-based pipeline and source-generated dispatch path, which trades raw per-call speed for a predictable, low-allocation footprint.

---

## Running Benchmarks Locally

```bash
cd tests/MediatorLite.Benchmarks
dotnet run --configuration Release -- --filter '*' --exporters json markdown --memory
```

Results are written to `BenchmarkDotNet.Artifacts/results/`.
