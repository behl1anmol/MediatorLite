# MediatorLite – AI Coding Guide

- **Architecture**
  - Core dispatcher lives in [src/MediatorLite/Internal/Mediator.cs](src/MediatorLite/Internal/Mediator.cs); it routes `IRequest<T>` to a single `IRequestHandler` and publishes `INotification` to all `INotificationHandler` implementations.
  - Two registration paths:
    - Source-generated (recommended): call `AddGeneratedHandlers()` (from [HandlerDiscoveryGenerator](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs)) then `AddMediatorLite()` for zero-reflection dispatch. Granular methods available: `AddGeneratedRequestHandlers()`, `AddGeneratedNotificationHandlers()`, `AddGeneratedBehaviors()`.
    - Manual DI: register handlers directly (e.g., `services.AddTransient<IRequestHandler<...>, Handler>()`) then call `AddMediatorLite()`. The mediator falls back to reflection-based dispatch with `ConcurrentDictionary` caching when `ISourceGeneratedMediator` is not registered.
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
  - Samples: manual DI registration sample in [samples/MediatorLite.Sample/Program.cs](samples/MediatorLite.Sample/Program.cs); source-gen sample in [samples/MediatorLite.Sample.SourceGen/Program.cs](samples/MediatorLite.Sample.SourceGen/Program.cs) showing `AddGeneratedHandlers` + performance logging behavior.
  - Source generator output class: `MediatorLite.Generated.MediatorLiteRegistration` exposes `AddGeneratedHandlers`, `AddGeneratedRequestHandlers`, `AddGeneratedNotificationHandlers`, `AddGeneratedBehaviors`, `RequestHandlerCount`, `NotificationHandlerCount`, and `BehaviorCount` for diagnostics.

- **Common pitfalls**
  - Forgetting to call `AddGeneratedHandlers()` or register handlers manually results in `InvalidOperationException` when sending requests.
  - Open generic behaviors registered via `MediatorOptions.AddOpenBehavior` are automatically added to DI by `AddMediatorLite()`. When using manual DI registration, register them directly (e.g., `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>))`).
  - When using parallel notification execution with `ContinueAndAggregate`, expect `AggregateException` wrapping handler failures.
