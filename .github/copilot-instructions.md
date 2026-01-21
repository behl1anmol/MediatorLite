# MediatorLite – AI Coding Guide

- **Architecture**
  - Core dispatcher lives in [src/MediatorLite/Internal/Mediator.cs](src/MediatorLite/Internal/Mediator.cs); it routes `IRequest<T>` to a single `IRequestHandler` and publishes `INotification` to all `INotificationHandler` implementations.
  - Two registration paths:
    - Runtime scan via [ServiceCollectionExtensions](src/MediatorLite/Configuration/ServiceCollectionExtensions.cs): call `AddMediatorLite` and `options.RegisterHandlersFromAssembly*` to scan assemblies.
    - Compile-time discovery via [HandlerDiscoveryGenerator](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs): call generated `AddGeneratedHandlers()` then `AddMediatorLiteCore` (or `AddMediatorLite` if you still want runtime options) to avoid reflection at startup.
  - Pipeline behaviors (`IPipelineBehavior<TRequest,TResponse>`) wrap handlers; they are resolved in DI registration order plus any types added through `MediatorOptions.BehaviorTypes`.
  - Notifications support execution strategies (sequential/parallel/stop-on-first) and error strategies (stop-first vs continue+aggregate) configured globally or per-notification.

- **Key options & attributes** (see [src/MediatorLite/Configuration/MediatorOptions.cs](src/MediatorLite/Configuration/MediatorOptions.cs) and [src/MediatorLite/Abstractions/Attributes.cs](src/MediatorLite/Abstractions/Attributes.cs))
  - `NotificationExecutionStrategy`, `NotificationErrorStrategy`, `EnableBuiltInLogging`, `EnableTracing`, `HandlerLifetime`, `MediatorLifetime`.
  - `[NotificationOptions]` overrides strategies per notification; `[NotificationHandlerOrder]` controls ordering; `[MediatorGeneration(Skip=true)]` omits a handler from source-gen registration.
  - `[MediatorLogging]` toggles logging or payload inclusion per request.

- **Behavior conventions**
  - Behaviors execute in the order registered in DI; the provided `BehaviorOrderAttribute` exists but mediator currently relies on registration order.
  - Behaviors may short-circuit by not calling `next()` (see [tests/MediatorLite.Tests/PipelineBehaviorTests.cs](tests/MediatorLite.Tests/PipelineBehaviorTests.cs)).
  - Open generic behaviors must be registered in DI (`services.AddTransient(typeof(LoggingBehavior<,>))`) as well as added via `MediatorOptions.AddOpenBehavior`.

- **Notifications**
  - Ordering is applied via `[NotificationHandlerOrder]` before executing handlers.
  - `PublishAsync` can run handlers sequentially, in parallel, or stop after the first handler completes; `NotificationErrorStrategy.ContinueAndAggregate` aggregates exceptions when parallel/sequential.

- **Validation**
  - Built-in `ValidationBehavior<TReq,TRes>` and `DataAnnotationsValidator<T>` are in [src/MediatorLite/Validation/Validation.cs](src/MediatorLite/Validation/Validation.cs); register validators in DI and add the open behavior to enforce.

- **Observability**
  - Tracing uses ActivitySource `MediatorLite` with tags like `mediatorlite.request.type`; enable via `EnableTracing` and add the source in OpenTelemetry setup (see [docs/observability.md](docs/observability.md)).
  - Built-in logging uses `ILogger` with default `LogLevel.Debug`; can be disabled globally or per-request.

- **Project conventions**
  - Target framework: net10.0 with nullable + implicit usings; warnings are treated as errors (see [Directory.Build.props](Directory.Build.props)).
  - Requests/handlers use `ValueTask`; commands with no return use `IRequest` and `Unit`.

- **Developer workflows**
  - Build/test: `dotnet test MediatorLite.sln` (runs xUnit + FluentAssertions tests under [tests/](tests)).
  - Samples: runtime DI sample in [samples/MediatorLite.Sample/Program.cs](samples/MediatorLite.Sample/Program.cs); source-gen sample in [samples/MediatorLite.Sample.SourceGen/Program.cs](samples/MediatorLite.Sample.SourceGen/Program.cs) showing `AddGeneratedHandlers` + performance logging behavior.
  - Source generator output class: `MediatorLite.Generated.MediatorLiteRegistration` exposes `AddGeneratedHandlers`, `RequestHandlerCount`, and `NotificationHandlerCount` for diagnostics.

- **Common pitfalls**
  - Forgetting to register handler assemblies or to call `AddGeneratedHandlers` results in `InvalidOperationException` when sending requests.
  - Behaviors must be registered both in options and DI; otherwise mediator will try to resolve them and find none.
  - When using parallel notification execution with `ContinueAndAggregate`, expect `AggregateException` wrapping handler failures.
