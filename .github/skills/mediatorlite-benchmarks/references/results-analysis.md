# Benchmark Results Analysis

## Environment

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.3 LTS (Noble Numbat)
AMD EPYC 7763 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3
  Job-YFEFPZ : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3

IterationCount=10  WarmupCount=3
```

## Full Results

### Simple Request

| Method | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|---:|---:|---:|
| MediatR_SimpleRequest | 118.8 ns | 1.60 ns | 1.06 ns | 1.00 | 0.0196 | 328 B | 1.00 |
| MediatorLite_SimpleRequest | 149.5 ns | 1.30 ns | 0.77 ns | 1.26 | 0.0153 | 256 B | 0.78 |

### Single Pipeline Behavior

| Method | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|---:|---:|---:|
| MediatR_WithBehavior | 226.3 ns | 0.49 ns | 0.26 ns | 1.00 | 0.0381 | 640 B | 1.00 |
| MediatorLite_WithBehavior | 300.1 ns | 3.21 ns | 2.13 ns | 1.33 | 0.0348 | 584 B | 0.91 |

### Multiple Pipeline Behaviors (3)

| Method | Mean | Error | StdDev | Ratio | RatioSD | Gen0 | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| MediatR_WithMultipleBehaviors | 384.4 ns | 4.99 ns | 2.97 ns | 1.00 | 0.01 | 0.0639 | 1,072 B | 1.00 |
| MediatorLite_WithMultipleBehaviors | 573.6 ns | 7.71 ns | 5.10 ns | 1.49 | 0.02 | 0.0505 | 856 B | 0.80 |

### Notifications

| Method | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|---:|---:|---:|
| MediatR_Notification | 211.6 ns | 2.40 ns | 1.59 ns | 1.00 | 0.0367 | 616 B | 1.00 |
| MediatorLite_Sequential_Notification | 267.5 ns | 1.56 ns | 1.03 ns | 1.26 | 0.0134 | 224 B | 0.36 |
| MediatorLite_Parallel_Notification | 346.3 ns | 1.06 ns | 0.70 ns | 1.64 | 0.0176 | 296 B | 0.48 |

## Consolidated Comparison

| Scenario | Time Δ | Alloc Δ | MediatorLite Alloc | MediatR Alloc | Alloc Saved |
|---|---|---|---|---|---|
| Simple Request | +26% | −22% | 256 B | 328 B | 72 B |
| Single Behavior | +33% | −9% | 584 B | 640 B | 56 B |
| Multiple Behaviors (3) | +49% | −20% | 856 B | 1,072 B | 216 B |
| Notification Sequential | +26% | −64% | 224 B | 616 B | 392 B |
| Notification Parallel | +64% | −52% | 296 B | 616 B | 320 B |

## Strengths

### Lower Memory Allocation (22–64% Reduction)

MediatorLite allocates less memory in every scenario. The savings come from:

1. **ValueTask-based pipeline** — `ValueTask<T>` avoids heap allocation for synchronously completed results. MediatR's `Task<T>` pipeline generates more state machine allocations.
2. **Source-generated dispatch** — direct method calls via `ISourceGeneratedMediator` avoid reflection-related allocations (no `MakeGenericType`, no `MethodInfo` caching).
3. **Notification handler resolution** — source-gen caches handler lists at compile time, avoiding runtime enumerable allocations.

The notification sequential scenario shows the strongest advantage: 224 B vs 616 B (64% less). Three handler invocations compound the per-call `ValueTask` savings.

### Allocation Savings Scale with Pipeline Depth

As more behaviors are added, the absolute allocation savings increase:

| Behaviors | MediatorLite | MediatR | Savings |
|---|---|---|---|
| 0 | 256 B | 328 B | 72 B |
| 1 | 584 B | 640 B | 56 B |
| 3 | 856 B | 1,072 B | 216 B |

The per-behavior allocation cost is approximately 200 B (MediatorLite) vs 248 B (MediatR), giving ~48 B savings per behavior layer added.

### Gen0 Collection Pressure

MediatorLite consistently shows lower Gen0 values (`Gen0` column), meaning fewer Gen0 garbage collections are triggered per 1,000 operations. Lower GC pressure translates to more predictable latency in high-throughput services.

## Weaknesses

### Raw Throughput

MediatorLite is slower in absolute time across all scenarios (26–64% overhead). The per-call latency gap is:

| Scenario | MediatorLite | MediatR | Δ |
|---|---|---|---|
| Simple Request | 149.5 ns | 118.8 ns | +30.7 ns |
| Single Behavior | 300.1 ns | 226.3 ns | +73.8 ns |
| Multiple Behaviors | 573.6 ns | 384.4 ns | +189.2 ns |
| Notification Seq | 267.5 ns | 211.6 ns | +55.9 ns |
| Notification Par | 346.3 ns | 211.6 ns | +134.7 ns |

In absolute terms these are nanosecond-scale differences — negligible compared to any real I/O operation (database call ~1ms, HTTP call ~10ms+). The overhead becomes relevant only in extremely hot paths with millions of operations per second.

### Parallel Notification Overhead for Trivial Handlers

The parallel notification strategy (346.3 ns, 296 B) is slower and allocates more than sequential (267.5 ns, 224 B) when handlers complete synchronously. The `Task.WhenAll` coordination and `ArrayPool<Task>` rental add overhead that only pays off when handlers perform real async I/O work.

## Interpretation Guide

### When Allocation Wins Matter

Low allocations matter most in:
- **High-throughput APIs** handling thousands of requests/second — GC pauses compound
- **Long-running services** where Gen2 promotion and LOH fragmentation accumulate
- **Memory-constrained environments** (containers with tight memory limits)
- **Latency-sensitive paths** where GC pauses cause tail-latency spikes

### When Raw Speed Matters

The throughput gap favors MediatR in:
- **CPU-bound hot loops** dispatching millions of requests with no I/O
- **Micro-benchmarks** — the gap is most visible when handlers do zero work

In practice, handler logic (database queries, HTTP calls, business rules) dominates execution time by orders of magnitude, making the dispatcher overhead difference negligible.

### The Behavior Pipeline Scaling Story

The time ratio increases from 1.26x (no behaviors) to 1.49x (3 behaviors), but the allocation savings also increase from 72 B to 216 B. In a typical production pipeline with 2–5 behaviors, MediatorLite provides meaningful allocation savings while the absolute time difference remains in the hundreds-of-nanoseconds range.

### Parallel vs Sequential Notifications

Choose based on handler behavior:
- **Sequential** (recommended default): Best for quick, synchronous handlers. Lowest overhead, lowest allocations.
- **Parallel**: Best for handlers with independent I/O work. The benchmark overhead (72 B extra, ~80 ns slower) disappears when handlers do real async work because `Task.WhenAll` enables genuine concurrency.

## How to Interpret BenchmarkDotNet Output Columns

| Column | Meaning |
|---|---|
| **Mean** | Arithmetic mean of all measured iterations. Primary comparison metric. |
| **Error** | Half of the 99.9% confidence interval. If `Mean ± Error` ranges don't overlap, the difference is statistically significant. |
| **StdDev** | Standard deviation across iterations. Lower values indicate more stable/repeatable measurements. |
| **Ratio** | `Mean / Baseline Mean`. Values >1.00 mean slower than baseline; <1.00 mean faster. |
| **RatioSD** | Standard deviation of the ratio, accounting for variance in both methods. |
| **Gen0** | Number of Gen0 garbage collections per 1,000 operations. Lower is better. |
| **Allocated** | Total bytes allocated per single operation on the managed heap. Primary memory metric. |
| **Alloc Ratio** | `Allocated / Baseline Allocated`. Values <1.00 mean less allocation than baseline. |

Tips for reading results:
- **Ratio close to 1.00** with overlapping error bars means no meaningful performance difference.
- **Alloc Ratio < 1.00** is a durable win — allocation savings are deterministic and not affected by CPU speed or system load.
- **Gen0 = 0** means the operation allocates less than ~8 KB (the threshold for triggering a Gen0 collection per 1,000 ops).
- When **Error > 10% of Mean**, the measurement is noisy — increase `iterationCount` or investigate system interference.
