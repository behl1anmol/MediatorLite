# MediatorLite – AI Coding Guide

- **Architecture**
  - The core dispatcher is the **generated** `MediatorLite.Generated.SourceGeneratedMediator` (emitted by [HandlerDiscoveryGenerator](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs)); it implements `IMediator` directly via a compile-time type-pattern switch with fully typed `ValueTask` pipelines (no reflection, no boxing). It routes `IRequest<T>` to a single `IRequestHandler` and publishes `INotification` to all `INotificationHandler` implementations by runtime type.
  - Single registration path: call `AddGeneratedHandlers()` — it registers all handlers/behaviors/validators **and** the generated `IMediator` itself. Granular methods available: `AddGeneratedRequestHandlers()`, `AddGeneratedNotificationHandlers()`, `AddGeneratedValidators()`, `AddGeneratedBehaviors()`. There is **no reflection fallback**: without the generator, `AddMediatorLite()`'s internal `ThrowingMediator` throws an `InvalidOperationException` with setup guidance on first use.
  - `AddMediatorLite()` takes no arguments, is optional (diagnostic fallback only), and call order relative to `AddGeneratedHandlers()` doesn't matter. The generated mediator is registered as `Scoped` (it captures the resolving scope's `IServiceProvider`); handler lifetimes are controlled by the consumer at their own DI registration.
  - `IMediator.SendAsync<T>` returns `ValueTask<T>` and `PublishAsync` returns `ValueTask` — consume each result exactly once; use `.AsTask()` for `Task.WhenAll`/fan-out.
  - Pipeline behaviors (`IPipelineBehavior<TRequest,TResponse>`) wrap handlers; they execute in `[BehaviorOrder]` order (lower first), with validation behaviors emitted first for validated request types.
  - Notifications support execution strategies (sequential/parallel/stop-on-first) and error strategies (stop-first vs continue+aggregate). These are resolved at compile time and baked into each generated `Publish_*` method.

- **Key attributes** (see [src/MediatorLite.Abstractions/Abstractions/Attributes.cs](src/MediatorLite.Abstractions/Abstractions/Attributes.cs))
  - Compile-time notification strategy attributes: `[NotificationExecution]` / `[NotificationError]` on notification types; `[assembly: DefaultNotificationExecution]` / `[assembly: DefaultNotificationError]` for assembly-wide defaults. Resolution precedence: per-type > assembly default > library default (`Sequential` / `StopOnFirstError`).
  - `[NotificationHandlerOrder]` controls ordering; `[BehaviorOrder]` orders pipeline behaviors (lower first); `[MediatorGeneration(Skip=true)]` is obsolete and only retained for legacy compatibility.
  - Observability opt-out (assembly-level, no-arg): `[assembly: DisableMediatorLogging]` and `[assembly: DisableMediatorTracing]` tell the generator to omit logging / tracing calls in generated code. Both are on by default.

- **Behavior conventions**
  - Behaviors execute in `[BehaviorOrder]` order (lower first); validation behaviors are emitted before other behaviors for validated request types.
  - Behaviors may short-circuit by not calling `next()` (see [tests/MediatorLite.Tests/SourceGeneration/PipelineBehaviorTests.cs](tests/MediatorLite.Tests/SourceGeneration/PipelineBehaviorTests.cs)).
  - Open generic behaviors are discovered by the source generator and expanded to **every** request/response pair at compile time; closed behaviors bind to a single request type. Never hand-register `services.AddTransient(typeof(IPipelineBehavior<,>), ...)` — the generator already did it and you'd double-register.

- **Notifications**
  - Ordering is applied via `[NotificationHandlerOrder]` before executing handlers.
  - `PublishAsync` can run handlers sequentially, in parallel, or stop after the first handler completes; `NotificationErrorStrategy.ContinueAndAggregate` aggregates exceptions when parallel/sequential. Strategy is resolved at compile time and baked into each generated `Publish_*` method.

- **Validation**
  - Built-in `ValidationBehavior<TReq,TRes>` and `DataAnnotationsValidator<T>` are in [src/MediatorLite/Validation/Validation.cs](src/MediatorLite/Validation/Validation.cs); the source generator auto-registers validators and wires `ValidationBehavior<,>` in front of other behaviors for validated types.

- **Observability**
  - Tracing uses `ActivitySource` `MediatorLite` with tags like `mediatorlite.request.type`. Add this source in your OpenTelemetry setup (see [docs/observability.md](docs/observability.md)). Tracing is on by default; opt out with `[assembly: DisableMediatorTracing]`.
  - Built-in logging emits `LogDebug` under the `MediatorLite.IMediator` category. Control the level via standard `Microsoft.Extensions.Logging` filters (e.g. `AddFilter("MediatorLite.IMediator", LogLevel.Information)`). Opt out entirely with `[assembly: DisableMediatorLogging]`.

- **Project conventions**
  - Target framework: net10.0 with nullable + implicit usings; warnings are treated as errors (see [Directory.Build.props](Directory.Build.props)).
  - Requests/handlers use `ValueTask`; commands with no return use `IRequest` and `Unit`.

- **Developer workflows**
  - Build/test: `dotnet test MediatorLite.sln` (runs xUnit + FluentAssertions tests under [tests/](tests)).
  - Sample: source-gen sample in [samples/MediatorLite.Sample.SourceGen/Program.cs](samples/MediatorLite.Sample.SourceGen/Program.cs) showing `AddGeneratedHandlers` + performance logging behavior, handler composition, closed/open behaviors, dual-layer validation, and ordered notifications. (The manual-DI sample was deleted in v2 — there is no manual dispatch path.)
  - Source generator output: `MediatorLite.Generated.MediatorLiteRegistration` exposes `AddGeneratedHandlers`, `AddGeneratedRequestHandlers`, `AddGeneratedNotificationHandlers`, `AddGeneratedValidators`, `AddGeneratedBehaviors`, and the diagnostic counts `RequestHandlerCount`, `NotificationHandlerCount`, `BehaviorCount`, `ValidatorCount`; `SourceGeneratedMediator` is the `IMediator` implementation.

- **Common pitfalls**
  - Forgetting to call `AddGeneratedHandlers()` results in `InvalidOperationException` (from the `ThrowingMediator` fallback) when sending requests.
  - Awaiting a `SendAsync`/`PublishAsync` result twice, or passing it to `Task.WhenAll` without `.AsTask()` — the `ValueTask` surface is single-consumption.
  - When using parallel notification execution with `ContinueAndAggregate`, expect `AggregateException` wrapping handler failures.

- **Knowledge skills** (detailed contextual guides for each project)
  - **mediatorlite-core** — Use when working on the runtime library (DI registration, `ThrowingMediator` fallback, diagnostics, validation runtime). Covers `AddMediatorLite()`, the generated dispatch story, the `[DisableMediatorLogging]` / `[DisableMediatorTracing]` assembly opt-outs, validation system, and observability.
  - **mediatorlite-abstractions** — Use when editing contracts or the public surface. Covers `IMediator` (ValueTask dispatch), `IRequest`, `IRequestHandler`, `INotification`, `INotificationHandler`, `IPipelineBehavior`, `Unit`, `IValidator`, and all attributes.
  - **mediatorlite-source-generation** — Use when working on the source generator, understanding generated code, extending handler/behavior/validator discovery, or fixing source-gen issues. Covers `HandlerDiscoveryGenerator`, the 4 discovery pipelines, generated registration methods, and the typed-switch `SourceGeneratedMediator` emission.
  - **mediatorlite-tests** — Use when writing or debugging tests. Covers test organization (`SourceGeneration/` vs `UnitTests/`), DI setup patterns, handler tracking via static state, assertion patterns, the `MediatorLiteRegistration.*Count` sanity checks, and the fact that `[MediatorGeneration(Skip=true)]` is obsolete.
  - **mediatorlite-sample-sourcegen** — Use when working on the source-gen sample or demonstrating full feature coverage. Covers handler composition, closed vs open behaviors, dual-layer validation, and ordered notifications with source-generated discovery.
  - **mediatorlite-benchmarks** — Use when writing benchmarks, interpreting performance results, or optimizing MediatorLite. Covers BenchmarkDotNet setup, comparison methodology vs MediatR, results analysis, allocation patterns, and throughput tradeoffs.
