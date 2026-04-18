# Instruction: Write and Run Benchmarks

## Intent

Add or update a BenchmarkDotNet benchmark and run it locally before merging, so performance claims are evidence-backed. The repo has two suites: **microbenchmarks** at [tests/MediatorLite.Benchmarks/](tests/MediatorLite.Benchmarks/) (MediatorLite vs. MediatR head-to-head) and **production-like REST benchmarks** at [tests/MediatorLite.RestApiBenchmarks/](tests/MediatorLite.RestApiBenchmarks/). CI runs both via [.github/workflows/benchmarks.yml](.github/workflows/benchmarks.yml) and the generated markdown is folded into [docs/benchmarks.md](docs/benchmarks.md) by [.github/scripts/update-benchmarks-doc.py](.github/scripts/update-benchmarks-doc.py).

## When to use

- Measuring the impact of a dispatch change, generator change, or new pipeline behavior.
- Validating that a "performance" optimisation is actually a win (allocations or throughput).
- Adding a new realistic scenario (fan-out, concurrent requests, large payloads) that the existing suites don't cover.

## Agent ownership

- **Primary:** `devops` — owns the benchmark suites and CI publishing.
- **Review gate:** `code-reviewer` reads the generated tables and flags regressions.
- **Author:** typically `backend-developer` drafts the benchmark alongside the change that motivated it.

## Inputs / Preconditions

- `.NET 10.0.x` SDK installed locally (matches CI env var `DOTNET_VERSION` in [.github/workflows/ci.yml](.github/workflows/ci.yml)).
- Release build is green: `dotnet build MediatorLite.sln -c Release` exits `0`.
- You understand that BenchmarkDotNet **must run in `Release`** with `--no-build` after a clean Release build, or results are meaningless.

## Numbered steps

1. **Pick the suite**:
   - **Microbenchmark** (single dispatch, nanosecond-scale): add a class or `[Benchmark]` method to [tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs](tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs).
   - **REST-level** (end-to-end, millisecond-scale): add to [tests/MediatorLite.RestApiBenchmarks/](tests/MediatorLite.RestApiBenchmarks/).

2. **Apply the standard attributes.** Every benchmark class must declare memory diagnostics, set a warmup + iteration count consistent with the existing suite, and identify a baseline method. The canonical shape:

   ```14:16:tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs
   [MemoryDiagnoser]
   [SimpleJob(warmupCount: 3, iterationCount: 10)]
   public class MediatorBenchmarks
   ```

   Pair one `[Benchmark(Baseline = true)]` with one or more `[Benchmark]` entries so BenchmarkDotNet produces `Ratio` and `Alloc Ratio` columns:

   ```232:242:tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs
       [Benchmark(Baseline = true)]
       public async Task<MediatRResult> MediatR_SimpleRequest()
       {
           return await _mediatr.Send(new MediatRQuery(1));
       }

       [Benchmark]
       public async Task<MediatorLiteResult> MediatorLite_SimpleRequest()
       {
           return await _mediatorLite.SendAsync(new MediatorLiteQuery(1));
       }
   ```

3. **Provision the DI container in `[GlobalSetup]`.** Use `NullLoggerFactory` so logging does not skew numbers:

   ```209:230:tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs
       [GlobalSetup]
       public void Setup()
       {
           // Setup MediatorLite with v2 source-gen dispatch
           // AddGeneratedHandlers() auto-registers all discovered handlers, behaviors, and SourceGeneratedMediator
           var mediatorLiteServices = new ServiceCollection();
           mediatorLiteServices.AddGeneratedHandlers();
           mediatorLiteServices.AddMediatorLite();
           mediatorLiteServices.AddSingleton<ILoggerFactory, NullLoggerFactory>();
           mediatorLiteServices.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
           _mediatorLiteProvider = mediatorLiteServices.BuildServiceProvider();
           _mediatorLite = _mediatorLiteProvider.GetRequiredService<MediatorLite.IMediator>();

           // Setup MediatR
           var mediatrServices = new ServiceCollection();
           mediatrServices.AddMediatR(cfg =>
           {
               cfg.RegisterServicesFromAssemblyContaining<MediatorBenchmarks>();
           });
           _mediatrProvider = mediatrServices.BuildServiceProvider();
           _mediatr = _mediatrProvider.GetRequiredService<MediatR.IMediator>();
       }
   ```

   Always dispose in `[GlobalCleanup]` to avoid cross-run contamination.

4. **Run locally — microbenchmarks**:

   ```powershell
   dotnet run -c Release --project tests/MediatorLite.Benchmarks -- --filter '*' --exporters json markdown --memory
   ```

   Expected output: BenchmarkDotNet runs each benchmark, prints a summary table, and writes artifacts to `tests/MediatorLite.Benchmarks/BenchmarkDotNet.Artifacts/results/`. Exit code `0` on success.

5. **Run locally — REST API benchmarks** (filter to the specific suite to save time):

   ```powershell
   dotnet run -c Release --project tests/MediatorLite.RestApiBenchmarks -- --filter '*RestApiReadWriteBenchmarks*'
   dotnet run -c Release --project tests/MediatorLite.RestApiBenchmarks -- --filter '*RestApiConcurrencyBenchmarks*'
   ```

   Both should exit `0` with a summary table at the end.

6. **Regenerate the docs snippet** (optional locally; CI does this automatically on `main`):

   ```powershell
   python3 .github/scripts/update-benchmarks-doc.py
   ```

   The script reads `BenchmarkDotNet.Artifacts/results/*-report-github.md`, rewrites [docs/benchmarks.md](docs/benchmarks.md), and exits `0`. If any of the four expected result files are missing, it prints `Missing result file: ... — skipping docs update.` and exits `0` without writing.

7. **CI behaviour.** The benchmark workflow is triggered on `push`/`pull_request` to `main` when `src/**` or the benchmark projects change, and via `workflow_dispatch`. It:
   - Runs both benchmark projects.
   - Uploads JSON + markdown as the `benchmark-results` artifact.
   - On push to `main`, regenerates `docs/benchmarks.md` and commits with `[skip ci]`.
   - On pull requests, posts a comment with the combined tables (see [.github/workflows/benchmarks.yml](.github/workflows/benchmarks.yml)).

## Validation / Acceptance

- Every new benchmark class has `[MemoryDiagnoser]` and at least one method marked `[Benchmark(Baseline = true)]`.
- Warmup + iteration counts match the existing suites (`warmupCount: 3, iterationCount: 10` for microbenchmarks) unless you have a justified reason to change them — document the reason in a code comment.
- Local run exited `0` and the summary table was attached to the PR description.
- No regression > 10% in mean time or > 5% in allocations vs. the baseline on `main`, unless the PR explicitly accepts a trade-off documented in the description.

## Handoff / Exit criteria

- `devops` confirms the PR's benchmark comment from CI matches the local summary (so CI runners aren't being flaky).
- If the benchmark is new, `devops` ensures it appears in the PR comment under the right sub-heading (Microbenchmarks vs. REST API Benchmarks) and — on merge — in [docs/benchmarks.md](docs/benchmarks.md).

## Related rules, skills, instructions

- Workflow: [.github/workflows/benchmarks.yml](.github/workflows/benchmarks.yml).
- Script: [.github/scripts/update-benchmarks-doc.py](.github/scripts/update-benchmarks-doc.py).
- Benchmarks: [tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs](tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs), [tests/MediatorLite.RestApiBenchmarks/](tests/MediatorLite.RestApiBenchmarks/).
- Published doc: [docs/benchmarks.md](docs/benchmarks.md).
- Agent: [.cursor/agents/orchestrator.md](.cursor/agents/orchestrator.md) (dispatches to `devops`).
- Related instructions: [add-new-pipeline-behavior.md](add-new-pipeline-behavior.md), [release-workflow.md](release-workflow.md).
