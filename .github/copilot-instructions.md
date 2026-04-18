# MediatorLite – AI Coding Guide

- **Architecture**
  - Core dispatcher lives in [src/MediatorLite/Internal/Mediator.cs](src/MediatorLite/Internal/Mediator.cs); it routes `IRequest<T>` to a single `IRequestHandler` and publishes `INotification` to all `INotificationHandler` implementations.
  - Two registration paths:
    - Source-generated (recommended): call `AddGeneratedHandlers()` (from [HandlerDiscoveryGenerator](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs)) then `AddMediatorLite()` for zero-reflection dispatch. Granular methods available: `AddGeneratedRequestHandlers()`, `AddGeneratedNotificationHandlers()`, `AddGeneratedBehaviors()`.
    - Manual DI: register handlers directly (e.g., `services.AddTransient<IRequestHandler<...>, Handler>()`) then call `AddMediatorLite()`. The mediator falls back to reflection-based dispatch with `ConcurrentDictionary` caching when `ISourceGeneratedMediator` is not registered.
  - `AddMediatorLite()` takes no arguments and always registers `IMediator` as `Transient`. Handler lifetimes are controlled by the consumer at their own DI registration.
  - Pipeline behaviors (`IPipelineBehavior<TRequest,TResponse>`) wrap handlers; they execute in `[BehaviorOrder]` order (lower first), with validation behaviors emitted first for validated request types.
  - Notifications support execution strategies (sequential/parallel/stop-on-first) and error strategies (stop-first vs continue+aggregate). These are resolved at compile time and baked into each generated `Publish_*` method.

- **Key attributes** (see [src/MediatorLite.Abstractions/Abstractions/Attributes.cs](src/MediatorLite.Abstractions/Abstractions/Attributes.cs))
  - Compile-time notification strategy attributes: `[NotificationExecution]` / `[NotificationError]` on notification types; `[assembly: DefaultNotificationExecution]` / `[assembly: DefaultNotificationError]` for assembly-wide defaults. Resolution precedence: per-type > assembly default > library default (`Sequential` / `StopOnFirstError`).
  - `[NotificationHandlerOrder]` controls ordering; `[BehaviorOrder]` orders pipeline behaviors (lower first); `[MediatorGeneration(Skip=true)]` is obsolete and only retained for legacy compatibility.
  - Observability opt-out (assembly-level, no-arg): `[assembly: DisableMediatorLogging]` and `[assembly: DisableMediatorTracing]` tell the generator to omit logging / tracing calls in generated code. Both are on by default.

- **Behavior conventions**
  - Behaviors execute in `[BehaviorOrder]` order (lower first); validation behaviors are emitted before other behaviors for validated request types.
  - Behaviors may short-circuit by not calling `next()` (see [tests/MediatorLite.Tests/PipelineBehaviorTests.cs](tests/MediatorLite.Tests/PipelineBehaviorTests.cs)).
  - Open generic behaviors are discovered and registered by the source generator; manual DI consumers register them directly (e.g., `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>))`).

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
  - Samples: manual DI registration sample in [samples/MediatorLite.Sample/Program.cs](samples/MediatorLite.Sample/Program.cs); source-gen sample in [samples/MediatorLite.Sample.SourceGen/Program.cs](samples/MediatorLite.Sample.SourceGen/Program.cs) showing `AddGeneratedHandlers` + performance logging behavior, handler composition, closed/open behaviors, dual-layer validation, and ordered notifications.
  - Source generator output class: `MediatorLite.Generated.MediatorLiteRegistration` exposes `AddGeneratedHandlers`, `AddGeneratedRequestHandlers`, `AddGeneratedNotificationHandlers`, `AddGeneratedBehaviors`, `RequestHandlerCount`, `NotificationHandlerCount`, and `BehaviorCount` for diagnostics.

- **Common pitfalls**
  - Forgetting to call `AddGeneratedHandlers()` or register handlers manually results in `InvalidOperationException` when sending requests.
  - When using manual DI registration, register open generic behaviors directly (e.g., `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>))`).
  - When using parallel notification execution with `ContinueAndAggregate`, expect `AggregateException` wrapping handler failures.

- **Knowledge skills** (detailed contextual guides for each project)
  - **mediatorlite-core** — Use when working on the core library abstractions, interfaces, mediator dispatch, DI registration, or configuration. Covers `IMediator`, `IRequest`, `IRequestHandler`, `INotification`, `INotificationHandler`, `IPipelineBehavior`, `ISourceGeneratedMediator`, `Unit`, attributes, `AddMediatorLite()`, the `[DisableMediatorLogging]` / `[DisableMediatorTracing]` assembly opt-outs, validation system, and observability.
  - **mediatorlite-source-generation** — Use when working on the source generator, understanding generated code, extending handler/behavior/validator discovery, or fixing source-gen issues. Covers `HandlerDiscoveryGenerator`, the 4 parallel pipelines, generated registration methods, dispatch switch patterns, and the `ISourceGeneratedMediator` contract.
  - **mediatorlite-testing** — Use when writing or debugging tests for any dispatch path (reflection or source-gen). Covers test organization (3 directories), DI setup patterns, the critical `[MediatorGeneration(Skip=true)]` convention, handler tracking, assertion patterns, and how to write new tests.
  - **mediatorlite-sample** — Use when working on the manual DI sample project or creating examples of reflection-based handler registration. Shows how to register handlers/behaviors explicitly without source generation.
  - **mediatorlite-sample-sourcegen** — Use when working on the source-gen sample or demonstrating full feature coverage. Covers handler composition, closed vs open behaviors, dual-layer validation, and ordered notifications with source-generated discovery.
  - **mediatorlite-benchmarks** — Use when writing benchmarks, interpreting performance results, or optimizing MediatorLite. Covers BenchmarkDotNet setup, comparison methodology vs MediatR, results analysis, allocation patterns, and throughput tradeoffs.
