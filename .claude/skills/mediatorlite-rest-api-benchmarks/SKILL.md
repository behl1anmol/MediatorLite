---
name: mediatorlite-rest-api-benchmarks
description: Reference for the MediatorLite.RestApiBenchmarks project -- a realistic ASP.NET Core + EF Core (SQLite) benchmark harness comparing MediatorLite vs MediatR under three axes (Mediator/Transport/Dataset/Concurrency), with ApiBenchmarkHost, BenchmarkParityGuard, SeedData, AppDbContext, parallel MediatorLite/MediatR handler sets, and shared pipeline behaviors.
triggers: REST API benchmarks, ApiBenchmarkHost, BenchmarkParityGuard, RestApiReadWriteBenchmarks, RestApiConcurrencyBenchmarks, ASP.NET Core benchmark, EF Core SQLite benchmark, BenchmarkSwitcher, MediatorImplementation, BenchmarkTransport, DatasetProfile, parity guard, minimal API benchmark, end-to-end mediator benchmark, OrderApplicationService
---

# MediatorLite.RestApiBenchmarks

## Purpose

`MediatorLite.RestApiBenchmarks` is a full ASP.NET Core minimal-API web application wrapped in BenchmarkDotNet. It compares MediatorLite against MediatR in a realistic scenario: EF Core (SQLite on disk), a shared `OrderApplicationService`, three pipeline behaviors, a three-handler notification, custom validation, and two HTTP transports (`TestServer` in-process vs `Kestrel` on `localhost`). A `BenchmarkParityGuard` validates at startup that both implementations have **identical** behavior counts, notification handler counts, validator counts, and seed data — so any difference in the benchmark output is attributable to the mediator itself, not to test drift.

## When to use

- Measuring end-to-end dispatch cost inside a real ASP.NET Core pipeline (minimal APIs, DI scopes, EF Core).
- Verifying that a change to the MediatorLite generator does not regress against MediatR in a production-shaped scenario.
- Adding a new realistic benchmark scenario (e.g. batched writes, streaming reads, high-concurrency hot keys).
- Understanding how to compose `[Params(...)]` combinations in BenchmarkDotNet for a matrix of benchmarks.
- Diagnosing parity drift — when benchmarks suddenly behave differently, `BenchmarkParityGuard` usually catches it at `GlobalSetup` time.

## Project location & entry points

- [MediatorLite.RestApiBenchmarks.csproj](tests/MediatorLite.RestApiBenchmarks/MediatorLite.RestApiBenchmarks.csproj) — `Microsoft.NET.Sdk.Web`, references `BenchmarkDotNet`, `MediatR`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.AspNetCore.TestHost`, and the MediatorLite runtime + generator.
- [Program.cs](tests/MediatorLite.RestApiBenchmarks/Program.cs) — top-level entry that runs `BenchmarkSwitcher`.
- [AssemblyInfo.cs](tests/MediatorLite.RestApiBenchmarks/AssemblyInfo.cs) — `[assembly: DisableMediatorLogging] [assembly: DisableMediatorTracing]`.
- [Hosting/ApiBenchmarkHost.cs](tests/MediatorLite.RestApiBenchmarks/Hosting/ApiBenchmarkHost.cs) — builds the web application, maps endpoints, chooses mediator.
- [Hosting/BenchmarkParityGuard.cs](tests/MediatorLite.RestApiBenchmarks/Hosting/BenchmarkParityGuard.cs) — startup assertions of behavior / handler / validator / seed parity.
- [Data/AppDbContext.cs](tests/MediatorLite.RestApiBenchmarks/Data/AppDbContext.cs) — EF Core entities (`Customer`, `Product`, `Order`, `OrderLine`, `AuditEntry`).
- [Data/SeedData.cs](tests/MediatorLite.RestApiBenchmarks/Data/SeedData.cs) — deterministic `Medium` / `Large` dataset seeders.
- [Benchmarking/BenchmarkModels.cs](tests/MediatorLite.RestApiBenchmarks/Benchmarking/BenchmarkModels.cs) — `MediatorImplementation` + `BenchmarkTransport` enums.
- [Benchmarking/RestApiReadWriteBenchmarks.cs](tests/MediatorLite.RestApiBenchmarks/Benchmarking/RestApiReadWriteBenchmarks.cs) — single-request scenarios.
- [Benchmarking/RestApiConcurrencyBenchmarks.cs](tests/MediatorLite.RestApiBenchmarks/Benchmarking/RestApiConcurrencyBenchmarks.cs) — `[Params(1, 8, 32)]` concurrent requests.
- [Application/Contracts/Requests.cs](tests/MediatorLite.RestApiBenchmarks/Application/Contracts/Requests.cs) — dual-`IRequest` records implementing **both** `ML.IRequest<T>` and `MR.IRequest<T>`.
- [Application/Common/OrderApplicationService.cs](tests/MediatorLite.RestApiBenchmarks/Application/Common/OrderApplicationService.cs) — shared real work (EF Core queries + transactions).
- [Application/MediatorLite/*.cs](tests/MediatorLite.RestApiBenchmarks/Application/MediatorLite/) — MediatorLite handlers + behaviors.
- [Application/MediatR/*.cs](tests/MediatorLite.RestApiBenchmarks/Application/MediatR/) — MediatR handlers + behaviors.

## Core types / API surface

### Entry point — `BenchmarkSwitcher`

```1:9:tests/MediatorLite.RestApiBenchmarks/Program.cs
using BenchmarkDotNet.Running;
using MediatorLite.RestApiBenchmarks.Benchmarking;

BenchmarkSwitcher.FromTypes(
[
    typeof(RestApiReadWriteBenchmarks),
    typeof(RestApiConcurrencyBenchmarks)
]).Run(args);
```

`BenchmarkSwitcher` (as opposed to `BenchmarkRunner`) accepts BenchmarkDotNet CLI args such as `--filter`, `--job`, `--exporters`. Both benchmark classes are pre-registered.

### Observability opt-out

```1:4:tests/MediatorLite.RestApiBenchmarks/AssemblyInfo.cs
using MediatorLite;

[assembly: DisableMediatorLogging]
[assembly: DisableMediatorTracing]
```

Same rationale as `MediatorLite.Benchmarks` — the generator emits the fast path with **no** try/catch, logger resolution, or `ActivitySource` calls so numbers reflect dispatch cost only.

### Benchmark axes — `BenchmarkModels.cs`

```1:14:tests/MediatorLite.RestApiBenchmarks/Benchmarking/BenchmarkModels.cs
namespace MediatorLite.RestApiBenchmarks.Benchmarking;

public enum MediatorImplementation
{
    MediatorLite = 0,
    MediatR = 1
}

public enum BenchmarkTransport
{
    InProcessTestServer = 0,
    LocalhostKestrel = 1
}
```

Combined with `DatasetProfile` (from `Data/SeedData.cs`) these enums expand the benchmark matrix via BenchmarkDotNet `[Params]`.

### `ApiBenchmarkHostFactory.CreateAsync` — pathway

```74:93:tests/MediatorLite.RestApiBenchmarks/Hosting/ApiBenchmarkHost.cs
        builder.Services.AddRouting();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        builder.Services.AddScoped<OrderApplicationService>();
        builder.Services.AddScoped<IAppValidator<CreateOrderCommand>, CreateOrderCommandValidator>();

        if (mediatorImplementation == MediatorImplementation.MediatorLite)
        {
            builder.Services.AddGeneratedHandlers();
            builder.Services.AddMediatorLite();
        }
        else
        {
            builder.Services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(ApiBenchmarkHostFactory).Assembly);
                cfg.AddOpenBehavior(typeof(MediatRValidationBehavior<,>));
                cfg.AddOpenBehavior(typeof(MediatRLoggingBehavior<,>));
                cfg.AddOpenBehavior(typeof(MediatRMetricsBehavior<,>));
            });
        }
```

Each scenario uses a **fresh SQLite file** in `%TEMP%` (`mediatorlite-rest-bench-<guid>.db`), which is seeded and then destroyed in `DisposeAsync`. Only one mediator is registered per container — the parity guard enforces this.

Endpoints are mapped in `MapEndpoints`, and the dispatch helpers `SendAsync<TResponse>` / `PublishAsync` pick the registered mediator at request time:

```200:224:tests/MediatorLite.RestApiBenchmarks/Hosting/ApiBenchmarkHost.cs
    private static async Task<TResponse> SendAsync<TResponse>(
        IServiceProvider serviceProvider,
        object request,
        CancellationToken cancellationToken)
    {
        if (serviceProvider.GetService<ML.IMediator>() is { } mediatorLite)
        {
            return await mediatorLite.SendAsync((ML.IRequest<TResponse>)request, cancellationToken);
        }

        var mediatR = serviceProvider.GetRequiredService<MR.IMediator>();
        return await mediatR.Send((MR.IRequest<TResponse>)request, cancellationToken);
    }

    private static async Task PublishAsync(IServiceProvider serviceProvider, object notification, CancellationToken cancellationToken)
    {
        if (serviceProvider.GetService<ML.IMediator>() is { } mediatorLite)
        {
            await mediatorLite.PublishAsync((ML.INotification)notification, cancellationToken);
            return;
        }

        var mediatR = serviceProvider.GetRequiredService<MR.IMediator>();
        await mediatR.Publish((MR.INotification)notification, cancellationToken);
    }
```

This is possible because requests implement **both** `ML.IRequest<T>` and `MR.IRequest<T>`:

```7:11:tests/MediatorLite.RestApiBenchmarks/Application/Contracts/Requests.cs
public sealed record CreateOrderCommand(
    [property: Range(1, int.MaxValue)] int CustomerId,
    [property: MinLength(1)] IReadOnlyList<CreateOrderLineInput> Lines,
    [property: StringLength(64, MinimumLength = 3)] string CorrelationId)
    : ML.IRequest<CreateOrderResult>, MR.IRequest<CreateOrderResult>;
```

### `BenchmarkParityGuard` — startup assertions

Called from `ApiBenchmarkHostFactory.CreateAsync` **after** seeding completes. It runs five checks:

```22:33:tests/MediatorLite.RestApiBenchmarks/Hosting/BenchmarkParityGuard.cs
    public static async Task ValidateAsync(
        IServiceProvider serviceProvider,
        MediatorImplementation mediatorImplementation,
        DatasetProfile datasetProfile,
        CancellationToken cancellationToken)
    {
        ValidateMediatorRegistration(serviceProvider, mediatorImplementation);
        ValidatePipelineParity(serviceProvider, mediatorImplementation);
        ValidateNotificationParity(serviceProvider, mediatorImplementation);
        ValidateValidationParity(serviceProvider);
        await ValidateDatasetParityAsync(serviceProvider, datasetProfile, cancellationToken);
    }
```

**Expected counts (hardcoded):** `expectedBehaviorCount = 3`, `expectedNotificationHandlers = 3`, `expectedValidatorCount = 1`. A violation throws `BenchmarkParityViolationException` before any benchmark iteration runs — failing fast.

```51:77:tests/MediatorLite.RestApiBenchmarks/Hosting/BenchmarkParityGuard.cs
    private static void ValidatePipelineParity(IServiceProvider serviceProvider, MediatorImplementation mediatorImplementation)
    {
        const int expectedBehaviorCount = 3;

        if (mediatorImplementation == MediatorImplementation.MediatorLite)
        {
            var behaviorCount = serviceProvider
                .GetServices<ML.IPipelineBehavior<CreateOrderCommand, CreateOrderResult>>()
                .Count();

            if (behaviorCount != expectedBehaviorCount)
            {
                throw new BenchmarkParityViolationException($"MediatorLite behavior count mismatch. Expected {expectedBehaviorCount}, got {behaviorCount}.");
            }

            return;
        }

        var mediatRBehaviorCount = serviceProvider
            .GetServices<MR.IPipelineBehavior<CreateOrderCommand, CreateOrderResult>>()
            .Count();

        if (mediatRBehaviorCount != expectedBehaviorCount)
        {
            throw new BenchmarkParityViolationException($"MediatR behavior count mismatch. Expected {expectedBehaviorCount}, got {mediatRBehaviorCount}.");
        }
    }
```

### `SeedData` — deterministic datasets

```13:18:tests/MediatorLite.RestApiBenchmarks/Data/SeedData.cs
    public static DatasetExpectedCounts GetExpectedCounts(DatasetProfile profile)
    {
        return profile == DatasetProfile.Large
            ? new DatasetExpectedCounts(2500, 4000, 18000, 25000)
            : new DatasetExpectedCounts(800, 1200, 6000, 10000);
    }
```

- `Medium`: 800 customers / 1200 products / 6000 orders / 10000 audit entries.
- `Large`: 2500 / 4000 / 18000 / 25000.

Seeding uses `new Random(42)` — deterministic across runs for reproducibility. Currently only `Medium` is listed as a `[Params]` value in the benchmark classes; `Large` is available for manual overrides.

### `AppDbContext` — EF Core model

5 entities with indexes and a `Customer→Orders→OrderLines→Product` graph:

```12:17:tests/MediatorLite.RestApiBenchmarks/Data/AppDbContext.cs
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
```

### `RestApiReadWriteBenchmarks` — single-request scenarios

```10:24:tests/MediatorLite.RestApiBenchmarks/Benchmarking/RestApiReadWriteBenchmarks.cs
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class RestApiReadWriteBenchmarks
{
    private ApiBenchmarkHost _host = null!;
    private HttpClient _client = null!;

    [Params(MediatorImplementation.MediatR, MediatorImplementation.MediatorLite)]
    public MediatorImplementation Mediator { get; set; }

    [Params(BenchmarkTransport.InProcessTestServer, BenchmarkTransport.LocalhostKestrel)]
    public BenchmarkTransport Transport { get; set; }

    [Params(DatasetProfile.Medium)]
    public DatasetProfile Dataset { get; set; }
```

Scenarios include `Read_OrderDetails` (baseline), `Read_SearchOrders`, `Read_SalesReport`, `Read_CustomerSummary`, `Write_CreateOrder`, `Write_CreateOrder_ValidationFailure`, `Write_CancelOrder`, `Write_UpdateOrderStatus_Conflict`.

### `RestApiConcurrencyBenchmarks` — matrix with `Concurrency`

```23:32:tests/MediatorLite.RestApiBenchmarks/Benchmarking/RestApiConcurrencyBenchmarks.cs
    [Params(DatasetProfile.Medium)]
    public DatasetProfile Dataset { get; set; }

    [Params(1, 8, 32)]
    public int Concurrency { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _host = await ApiBenchmarkHostFactory.CreateAsync(Mediator, Transport, Dataset, CancellationToken.None);
        _client = _host.Client;
    }
```

Each benchmark fires `Concurrency` HTTP requests via `Task.WhenAll` and counts 200s.

### Three MediatorLite behaviors (auto-discovered by the generator)

```6:40:tests/MediatorLite.RestApiBenchmarks/Application/MediatorLite/MediatorLiteBehaviors.cs
public sealed class MediatorLiteValidationBehavior<TRequest, TResponse> : ML.IPipelineBehavior<TRequest, TResponse>
    where TRequest : ML.IRequest<TResponse>
{
    private readonly IEnumerable<IAppValidator<TRequest>> _validators;

    public MediatorLiteValidationBehavior(IEnumerable<IAppValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        ML.RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var errors = new List<string>();
        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            errors.AddRange(result);
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }

        return await next();
    }
}
```

`MediatorLiteLoggingBehavior` and `MediatorLiteMetricsBehavior` are trivial pass-throughs used purely to add depth to the pipeline (matching the three MediatR behaviors).

Note: this project does **not** use the built-in `MediatorLite.ValidationBehavior` / `IValidator<T>` contract — it defines its own `IAppValidator<TRequest>` contract so both MediatorLite and MediatR hit an identical validation codepath via the parallel behaviors.

### Three notification handlers with explicit order

```112:127:tests/MediatorLite.RestApiBenchmarks/Application/MediatorLite/MediatorLiteHandlers.cs
[ML.NotificationHandlerOrder(1)]
public sealed class MediatorLiteOrderAuditNotificationHandler : ML.INotificationHandler<OrderCreatedNotification>
{
    private readonly OrderApplicationService _applicationService;

    public MediatorLiteOrderAuditNotificationHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async ValueTask HandleAsync(OrderCreatedNotification notification, CancellationToken cancellationToken = default)
    {
        var payload = $"{{\"orderId\":{notification.OrderId},\"customerId\":{notification.CustomerId}}}";
        await _applicationService.WriteAuditAsync("ORDER_CREATED_NOTIFICATION", payload, cancellationToken);
    }
}
```

Three ordered handlers (`1`, `2`, `3`) write audit rows — real I/O work on the SQLite DB — so the benchmark exercises the full publish path including EF Core persistence.

## Patterns & invariants

**Do:**
- Implement **both** `ML.IRequest<T>` and `MR.IRequest<T>` on every request record; the endpoint dispatcher casts based on which mediator is present.
- Register exactly **three** pipeline behaviors on both sides (`Validation`, `Logging`, `Metrics`). `BenchmarkParityGuard` will fail startup otherwise.
- Register exactly **three** handlers for `OrderCreatedNotification` on both sides.
- Use `[Params(...)]` for enum axes so BenchmarkDotNet generates one row per combination.
- Keep `DisableMediatorLogging` / `DisableMediatorTracing` at assembly level — MediatR has no equivalent, so leaving MediatorLite's observability on would bias the benchmark.
- Delete the SQLite file in `ApiBenchmarkHost.DisposeAsync` to avoid temp bloat.
- Use `Random(42)` in `SeedData` for deterministic inter-run comparability.

**Don't:**
- Don't register both MediatorLite and MediatR in the same container; `BenchmarkParityGuard.ValidateMediatorRegistration` will throw.
- Don't use `[MediatorGeneration(Skip=true)]` — it is obsolete and the generator discovers handlers unconditionally.
- Don't add behaviors/handlers to only one side without updating the parity guard's `expected*Count` constants.
- Don't replace `Microsoft.AspNetCore.TestHost` with an in-proc mock HTTP handler — the benchmark intentionally goes through the full Kestrel/TestServer pipeline.
- Don't seed non-deterministically (e.g. `new Random()` without seed) — results will drift between CI runs.

## Common tasks

1. **Run the full benchmark matrix**
   ```
   dotnet run -c Release --project tests/MediatorLite.RestApiBenchmarks
   ```
   BenchmarkSwitcher will prompt for a class if you don't pass `--filter`.

2. **Run a single benchmark class**
   ```
   dotnet run -c Release --project tests/MediatorLite.RestApiBenchmarks -- --filter '*RestApiReadWriteBenchmarks*'
   ```

3. **Run only MediatorLite + Kestrel combos**
   ```
   dotnet run -c Release --project tests/MediatorLite.RestApiBenchmarks -- --filter '*RestApiReadWriteBenchmarks*' --anyCategories Mediator=MediatorLite Transport=LocalhostKestrel
   ```
   (BDN does not directly filter by `[Params]` values; alternatively temporarily narrow `[Params(...)]` on the benchmark class.)

4. **Add a new endpoint + benchmark scenario**
   1. Add the request record to [Application/Contracts/Requests.cs](tests/MediatorLite.RestApiBenchmarks/Application/Contracts/Requests.cs) implementing **both** `ML.IRequest<T>` and `MR.IRequest<T>`.
   2. Add DTOs to [Application/Contracts/Models.cs](tests/MediatorLite.RestApiBenchmarks/Application/Contracts/Models.cs).
   3. Add the work method to `OrderApplicationService` — this is the single source of truth for the logic.
   4. Add handlers in **both** [Application/MediatorLite/MediatorLiteHandlers.cs](tests/MediatorLite.RestApiBenchmarks/Application/MediatorLite/MediatorLiteHandlers.cs) and [Application/MediatR/MediatRHandlers.cs](tests/MediatorLite.RestApiBenchmarks/Application/MediatR/MediatRHandlers.cs) — each delegates to `OrderApplicationService`.
   5. Map the endpoint in `ApiBenchmarkHost.MapEndpoints`.
   6. Add a `[Benchmark]` method in `RestApiReadWriteBenchmarks` (or `RestApiConcurrencyBenchmarks`) that calls the HTTP endpoint.

5. **Bump the dataset to `Large`**
   - Change `[Params(DatasetProfile.Medium)]` to `[Params(DatasetProfile.Large)]` or include both.
   - The parity guard reads expected counts from `SeedData.GetExpectedCounts(datasetProfile)` so no manual update is needed.

6. **Diagnose a `BenchmarkParityViolationException`**
   - Read the message — it tells you which check failed and the expected/actual counts.
   - Most common cause: you added a handler or behavior on one side but not the other, or you changed the `expected*Count` constant without matching the registrations.

## Pitfalls & gotchas

- **SQLite file-on-disk vs in-memory**: the harness uses disk SQLite (`Data Source=<temp>.db`), not `:memory:`. File I/O is part of the measurement — this is intentional for realism but increases variance on slow disks. Use a fast SSD / tmpfs for reproducible results.
- **`TestServer` vs `Kestrel`** results diverge significantly: `TestServer` skips the socket layer entirely. Both are reported because you usually want to measure both.
- **Boxing on value-type responses** applies here too — see the note in [ISourceGeneratedMediator.cs](src/MediatorLite.Abstractions/Abstractions/ISourceGeneratedMediator.cs). This benchmark mostly returns reference-type DTOs (records), so boxing is minimized.
- **`DbContext` is `Scoped`**: each HTTP request gets its own scope; concurrency benchmarks rely on this — do not make `AppDbContext` a singleton.
- **`Write_CreateOrder_ValidationFailure`** deliberately fails validation (duplicate `ProductId`) and returns 400 — its cost measures the **validation short-circuit** path (exception thrown in behavior, not reaching the handler).
- **`Concurrent_*` counts successful requests** — if your new write touches shared stock and causes contention, the success count may drop. `_applicationService.CreateOrderAsync` uses `BeginTransactionAsync` which serializes SQLite writes; concurrent **write** benchmarks would therefore largely measure lock contention.
- **`BenchmarkSwitcher` vs `BenchmarkRunner`**: `BenchmarkSwitcher` passes through CLI args, which is why `Program.cs` forwards `args`. Do not replace it with `BenchmarkRunner.Run<>` unless you also remove CLI forwarding.
- **Custom validation contract**: the benchmark uses `IAppValidator<T>` / `AppValidationException`, **not** MediatorLite's built-in `IValidator<T>` / `ValidationException`. This is deliberate so the comparison is framework-neutral — don't "fix" this to use MediatorLite's built-in validation unless you also replace MediatR's equivalent.

## Related skills & rules

- **mediatorlite-abstractions** — `ML.IRequest<T>`, `ML.IPipelineBehavior<,>`, `[NotificationHandlerOrder]`, `[DisableMediatorLogging]`, `[DisableMediatorTracing]` used here.
- **mediatorlite-core** — `AddMediatorLite()` runtime exercised by the harness.
- **mediatorlite-source-generation** — the `AddGeneratedHandlers()` / `AddMediatorLite()` pair registers the generated dispatch tables referenced by every benchmark request.
- **mediatorlite-benchmarks** — sibling microbenchmark project; compare when deciding whether an observed regression is real (macro) or just microbenchmark noise.
- **mediatorlite-sample-sourcegen** — shows a simpler console setup of the same `AddGeneratedHandlers` wiring.
- Docs: [docs/benchmarks.md](docs/benchmarks.md) (published benchmark results), [docs/observability.md](docs/observability.md).
