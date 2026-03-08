# Benchmark Methodology

## Benchmark Configuration

All four benchmark classes use identical BenchmarkDotNet attributes:

```csharp
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
```

- **`[MemoryDiagnoser]`** — Tracks Gen0/Gen1/Gen2 collections and total bytes allocated per operation. This is the primary metric for comparing allocation profiles.
- **`[SimpleJob(warmupCount: 3, iterationCount: 10)]`** — 3 warmup iterations (JIT compilation, tiered compilation stabilization) followed by 10 measured iterations. Keeps benchmark runs fast while still producing statistically meaningful results.

The entry point runs all four benchmark classes sequentially:

```csharp
BenchmarkRunner.Run<MediatorBenchmarks>();
BenchmarkRunner.Run<PipelineBenchmarks>();
BenchmarkRunner.Run<NotificationBenchmarks>();
BenchmarkRunner.Run<MultipleBehaviorsBenchmarks>();
```

## Handler Design: Zero-Work Handlers

Every handler in the benchmark file does zero application work. This is intentional — the benchmarks measure **dispatcher and pipeline overhead**, not application logic.

MediatorLite handlers return synchronously completed `ValueTask`:

```csharp
public ValueTask<MediatorLiteResult> HandleAsync(MediatorLiteQuery request, CancellationToken cancellationToken = default)
{
    return ValueTask.FromResult(new MediatorLiteResult(request.Id, "Test"));
}
```

MediatR handlers return synchronously completed `Task`:

```csharp
public Task<MediatRResult> Handle(MediatRQuery request, CancellationToken cancellationToken)
{
    return Task.FromResult(new MediatRResult(request.Id, "Test"));
}
```

Notification handlers return `ValueTask.CompletedTask` (MediatorLite) or `Task.CompletedTask` (MediatR).

Pipeline behaviors call `await next()` immediately — no logging, no validation, no metrics collection. This isolates behavior pipeline construction and delegate chain overhead.

## Setup Pattern

Each benchmark class follows the same DI setup pattern in `[GlobalSetup]`:

### MediatorLite Setup

```csharp
var mediatorLiteServices = new ServiceCollection();
mediatorLiteServices.AddSingleton<ISourceGeneratedMediator, SourceGeneratedMediator>();
mediatorLiteServices.AddTransient<IRequestHandler<MediatorLiteQuery, MediatorLiteResult>, MediatorLiteHandler>();
// Behaviors registered here when applicable:
// mediatorLiteServices.AddTransient(typeof(IPipelineBehavior<,>), typeof(MediatorLiteLoggingBehavior<,>));
mediatorLiteServices.AddMediatorLite(options =>
{
    options.EnableBuiltInLogging = false;
    options.EnableTracing = false;
});
mediatorLiteServices.AddSingleton<ILoggerFactory, NullLoggerFactory>();
mediatorLiteServices.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
_mediatorLiteProvider = mediatorLiteServices.BuildServiceProvider();
_mediatorLite = _mediatorLiteProvider.GetRequiredService<IMediator>();
```

Key points:
- `ISourceGeneratedMediator` is registered as **singleton** — the `SourceGeneratedMediator` class comes from `MediatorLite.Generated` namespace (source-generated).
- Handlers are registered as **transient** — consistent with production usage.
- Built-in logging and tracing are **disabled** to avoid measuring observability overhead.
- `NullLoggerFactory` and `NullLogger<>` prevent any logging allocation.

### MediatR Setup

```csharp
var mediatrServices = new ServiceCollection();
mediatrServices.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<MediatorBenchmarks>();
    // Behaviors added via cfg.AddOpenBehavior() when applicable
});
_mediatrProvider = mediatrServices.BuildServiceProvider();
_mediatr = _mediatrProvider.GetRequiredService<MediatR.IMediator>();
```

`RegisterServicesFromAssemblyContaining` scans the assembly for all MediatR handler types.

### NotificationBenchmarks Setup

The notification benchmark is unique — it builds **three** separate `ServiceProvider` instances:

1. MediatorLite with `NotificationExecutionStrategy.Sequential`
2. MediatorLite with `NotificationExecutionStrategy.Parallel`
3. MediatR (default sequential behavior)

Each registers three notification handler types (`NotificationHandler1`, `NotificationHandler2`, `NotificationHandler3`).

### Cleanup

All benchmark classes implement `[GlobalCleanup]` to dispose `ServiceProvider` instances:

```csharp
[GlobalCleanup]
public void Cleanup()
{
    (_mediatorLiteProvider as IDisposable)?.Dispose();
    (_mediatrProvider as IDisposable)?.Dispose();
}
```

## All Inline Types

All request, handler, behavior, and notification types are defined as nested classes inside `MediatorBenchmarks`. The other benchmark classes (`PipelineBenchmarks`, `MultipleBehaviorsBenchmarks`, `NotificationBenchmarks`) reference them via `MediatorBenchmarks.MediatorLiteQuery`, `MediatorBenchmarks.MediatRQuery`, etc.

MediatorLite types:
- `MediatorLiteQuery(int Id) : IRequest<MediatorLiteResult>` — record request
- `MediatorLiteResult(int Id, string Name)` — record response
- `MediatorLiteHandler` — request handler
- `MediatorLiteLoggingBehavior<TRequest, TResponse>` — open generic, no-op
- `MediatorLiteValidationBehavior<TRequest, TResponse>` — open generic, no-op
- `MediatorLiteMetricsBehavior<TRequest, TResponse>` — open generic, no-op
- `MediatorLiteNotification(int Id) : INotification` — record notification
- `MediatorLiteNotificationHandler1`, `2`, `3` — return `ValueTask.CompletedTask`

MediatR types (mirror of above):
- `MediatRQuery(int Id) : MediatR.IRequest<MediatRResult>`
- `MediatRResult(int Id, string Name)`
- `MediatRHandler` — returns `Task.FromResult`
- `MediatRLoggingBehavior<,>`, `MediatRValidationBehavior<,>`, `MediatRMetricsBehavior<,>` — return `Task`
- `MediatRNotification(int Id) : MediatR.INotification`
- `MediatRNotificationHandler1`, `2`, `3` — return `Task.CompletedTask`

## ValueTask vs Task Impact on Allocations

MediatorLite uses `ValueTask` throughout its handler and behavior interfaces; MediatR uses `Task`. This difference is a significant contributor to the allocation gap:

- **`ValueTask.FromResult`** does not allocate heap memory for synchronously completed results — the value is stored inline in the struct.
- **`Task.FromResult`** returns a cached `Task<T>` for common values but still allocates for arbitrary result types.
- **`ValueTask.CompletedTask`** is allocation-free. `Task.CompletedTask` is cached but each behavior's `async Task<T>` method still generates a state machine allocation.
- Each `async ValueTask<T>` behavior in MediatorLite also generates a state machine, but `IValueTaskSource` pooling can reduce allocations in high-throughput scenarios.

The allocation difference is most visible in the notification scenario (224 B vs 616 B for sequential) because three handler invocations compound the per-call savings.

## How to Run Locally

Run all benchmarks:

```bash
cd tests/MediatorLite.Benchmarks
dotnet run --configuration Release -- --filter '*' --exporters json markdown --memory
```

Run a specific class:

```bash
dotnet run --configuration Release -- --filter '*NotificationBenchmarks*'
```

Run a specific method:

```bash
dotnet run --configuration Release -- --filter '*MediatorLite_SimpleRequest*'
```

Export results in multiple formats:

```bash
dotnet run --configuration Release -- --filter '*' --exporters json markdown csv html --memory
```

Results are written to `BenchmarkDotNet.Artifacts/results/` relative to the project directory. CI results are also archived at the repository root in `BenchmarkDotNet.Artifacts/results/`.

Important: always use `--configuration Release` — benchmarks in Debug mode produce misleading results due to disabled optimizations and tiered compilation differences.
