# MediatorLite REST API Benchmarks

This project benchmarks realistic REST API workloads using ASP.NET Core and SQLite to compare:

- MediatorLite (source-generated registration path)
- MediatR (default registration path)

It complements microbenchmarks by measuring end-to-end request behavior, including HTTP routing, JSON serialization, EF Core query/transaction work, pipeline behaviors, and notification fan-out.

## Benchmark Dimensions

- Mediator implementation: MediatorLite vs MediatR
- Transport mode: in-process TestServer vs localhost Kestrel
- Dataset profile: medium (default)
- Concurrency levels: 1, 8, 32 (concurrency benchmark class)

## Run Benchmarks

From repository root:

```bash
dotnet run -c Release --project tests/MediatorLite.RestApiBenchmarks -- --filter '*RestApiReadWriteBenchmarks*'
```

Run concurrency-only benchmarks:

```bash
dotnet run -c Release --project tests/MediatorLite.RestApiBenchmarks -- --filter '*RestApiConcurrencyBenchmarks*'
```

## Notes

- Database is seeded deterministically per benchmark host setup.
- Logging providers are cleared to avoid benchmark skew.
- MediatorLite uses AddGeneratedHandlers + AddMediatorLite.
- MediatR uses AddMediatR + open behaviors.
- Benchmark output is written under BenchmarkDotNet.Artifacts/results.
- Fairness and parity requirements are documented in FAIRNESS_CHECKLIST.md.
- Startup parity checks are enforced by BenchmarkParityGuard and fail fast on drift.
