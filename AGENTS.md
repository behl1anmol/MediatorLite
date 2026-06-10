# MediatorLite Agent Guide

- Start with `.github/copilot-instructions.md`; use this file as the short repo map.
- Role-specific guidance lives in `.github/agents/*.agent.md` (`code-reviewer`, `dotnet-self-learning-architect`).

## Big picture
- Core dispatch is the **generated** `MediatorLite.Generated.SourceGeneratedMediator`, emitted by `src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs`. It implements `IMediator` directly — there is no runtime `Mediator` wrapper class and no `ISourceGeneratedMediator` interface.
- `SendAsync<T>` routes one `IRequest<T>` to a single `IRequestHandler<,>` via a compile-time type-pattern switch (fully typed `ValueTask` pipelines, no boxing); `PublishAsync` fans out `INotification` to all matching handlers by runtime type.
- Source-generated: call `AddGeneratedHandlers()` (registers the generated `IMediator` itself); `AddMediatorLite()` is an optional diagnostic fallback and call order does not matter. Use the granular `AddGeneratedRequestHandlers()`, `AddGeneratedNotificationHandlers()`, `AddGeneratedValidators()`, and `AddGeneratedBehaviors()` methods when you need partial registration.
- There is no reflection fallback; without the generator, dispatch throws via the internal `ThrowingMediator` with setup guidance.

## Important patterns
- DI registration lives in `src/MediatorLite/Configuration/ServiceCollectionExtensions.cs`. `AddMediatorLite()` takes no arguments; the generated mediator is registered as `Scoped` (it captures the resolving scope's `IServiceProvider`).
- Public attributes are in `src/MediatorLite.Abstractions/Abstractions/Attributes.cs`.
- Source-generation entry point is `src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs`; generated diagnostics surface as `MediatorLite.Generated.MediatorLiteRegistration`.
- `MediatorLiteRegistration` exposes `AddGeneratedHandlers()`, the granular `AddGeneratedRequestHandlers()` / `AddGeneratedNotificationHandlers()` / `AddGeneratedValidators()` / `AddGeneratedBehaviors()` methods, and diagnostic counts (`RequestHandlerCount`, `NotificationHandlerCount`, `BehaviorCount`, `ValidatorCount`).
- Behaviors execute in `[BehaviorOrder]` order; lower values run first, and validation behaviors are emitted before other behaviors for validated request types.
- Behaviors may short-circuit by not calling `next()`.
- Notifications honor `NotificationHandlerOrderAttribute`. Execution and error strategies are **compile-time only** via the per-notification `NotificationExecutionAttribute` / `NotificationErrorAttribute` and the assembly-level `DefaultNotificationExecutionAttribute` / `DefaultNotificationErrorAttribute`. The generator resolves them (per-notification > assembly default > library default: `Sequential` / `StopOnFirstError`) and inlines the result into each `Publish_*` method as a single branch-free path. The old `NotificationOptionsAttribute` and its runtime counterparts have been removed.

## Validation and observability
- Validation lives in `src/MediatorLite/Validation/Validation.cs`; source-gen registration auto-discovers custom `IValidator<T>` implementations, registers `ValidationBehavior<,>`, and adds `DataAnnotationsValidator<T>` for annotated request types.
- Built-in logging and tracing are **on by default** and emitted inline by the generator into each `Send_*` / `Publish_*` method. Opt out at compile time via `[assembly: DisableMediatorLogging]` / `[assembly: DisableMediatorTracing]` (both no-arg attributes in the `MediatorLite` namespace). The generator simply omits the corresponding calls when the attributes are present.
- Log level is controlled through standard `Microsoft.Extensions.Logging` configuration (e.g. `AddFilter("MediatorLite.IMediator", LogLevel.X)` or `appsettings.json`). Generated code always calls `LogDebug`.
- The deleted `MediatorLoggingAttribute` (per-class `Enabled` / `IncludePayload` / `LogLevel`) was never consumed and is no longer part of the public surface.
- Observability tags and OpenTelemetry setup are documented in `docs/observability.md`.

## Project conventions
- Target framework is `net10.0`; nullable and implicit usings are enabled; warnings are treated as errors (`Directory.Build.props`).
- The mediator surface and handlers/behaviors are `ValueTask`/`ValueTask<T>` end-to-end; consume each result exactly once and use `.AsTask()` for `Task.WhenAll`/fan-out.
- Use `IRequest<Unit>` for commands with no response.
- `[MediatorGeneration(Skip = true)]` is obsolete and retained only for legacy compatibility; avoid using it for new code.

## Workflows
- Build/test with `dotnet test MediatorLite.sln`.
- Check `samples/MediatorLite.Sample.SourceGen/Program.cs` for the full source-generated path.
- Test layout: `tests/MediatorLite.Tests/{SourceGeneration,UnitTests}`.
- `MediatorLiteRegistration.RequestHandlerCount`, `NotificationHandlerCount`, `BehaviorCount`, and `ValidatorCount` are useful sanity checks when validating source-generation coverage.
- When changing dispatch logic, keep the generated `SourceGeneratedMediator` switch arms, `Send_*`/`Publish_*` methods, and request/notification/behavior/validator registration aligned (see `.claude/rules/10-dispatch-invariants.md`).

