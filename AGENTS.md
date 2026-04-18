# MediatorLite Agent Guide

- Start with `.github/copilot-instructions.md`; use this file as the short repo map.
- Role-specific guidance lives in `.github/agents/*.agent.md` (`code-reviewer`, `dotnet-self-learning-architect`).

## Big picture
- Core dispatch is in `src/MediatorLite/Internal/Mediator.cs`.
- `SendAsync<T>` routes one `IRequest<T>` to a single `IRequestHandler<,>`; `PublishAsync` fans out `INotification` to all matching handlers.
- Source-generated: call `AddGeneratedHandlers()` then `AddMediatorLite()` for the supported runtime path; use the granular `AddGeneratedRequestHandlers()`, `AddGeneratedNotificationHandlers()`, `AddGeneratedValidators()`, and `AddGeneratedBehaviors()` methods when you need partial registration.
- `Mediator.cs` depends on `ISourceGeneratedMediator`; do not rely on reflection fallback or manual handler registration for dispatch.

## Important patterns
- DI registration lives in `src/MediatorLite/Configuration/ServiceCollectionExtensions.cs`.
- Runtime knobs are in `src/MediatorLite/Configuration/MediatorOptions.cs`.
- Public attributes are in `src/MediatorLite.Abstractions/Abstractions/Attributes.cs`.
- Source-generation entry point is `src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs`; generated diagnostics surface as `MediatorLite.Generated.MediatorLiteRegistration`.
- `MediatorLiteRegistration` exposes `AddGeneratedHandlers()`, the granular `AddGeneratedRequestHandlers()` / `AddGeneratedNotificationHandlers()` / `AddGeneratedValidators()` / `AddGeneratedBehaviors()` methods, and diagnostic counts (`RequestHandlerCount`, `NotificationHandlerCount`, `BehaviorCount`, `ValidatorCount`).
- Behaviors execute in `[BehaviorOrder]` order; lower values run first, and validation behaviors are emitted before other behaviors for validated request types.
- Behaviors may short-circuit by not calling `next()`.
- Notifications honor `NotificationHandlerOrderAttribute`. Execution and error strategies are **compile-time only** via the per-notification `NotificationExecutionAttribute` / `NotificationErrorAttribute` and the assembly-level `DefaultNotificationExecutionAttribute` / `DefaultNotificationErrorAttribute`. The generator resolves them (per-notification > assembly default > library default: `Sequential` / `StopOnFirstError`) and inlines the result into each `Publish_*` method as a single branch-free path. The old `NotificationOptionsAttribute` and the `MediatorOptions.NotificationExecutionStrategy` / `NotificationErrorStrategy` runtime properties have been removed.

## Validation and observability
- Validation lives in `src/MediatorLite/Validation/Validation.cs`; source-gen registration auto-discovers custom `IValidator<T>` implementations, registers `ValidationBehavior<,>`, and adds `DataAnnotationsValidator<T>` for annotated request types.
- Built-in logging and tracing are controlled by `MediatorOptions.EnableBuiltInLogging` and `EnableTracing`.
- Observability tags and OpenTelemetry setup are documented in `docs/observability.md`.

## Project conventions
- Target framework is `net10.0`; nullable and implicit usings are enabled; warnings are treated as errors (`Directory.Build.props`).
- Public APIs use `Task`/`Task<T>` for the mediator surface, but handlers and behaviors use `ValueTask`.
- Use `IRequest<Unit>` for commands with no response.
- `[MediatorGeneration(Skip = true)]` is obsolete and retained only for legacy compatibility; avoid using it for new code.

## Workflows
- Build/test with `dotnet test MediatorLite.sln`.
- Check `samples/MediatorLite.Sample.SourceGen/Program.cs` for the full source-generated path.
- Test layout: `tests/MediatorLite.Tests/{SourceGeneration,UnitTests}`.
- `MediatorLiteRegistration.RequestHandlerCount`, `NotificationHandlerCount`, `BehaviorCount`, and `ValidatorCount` are useful sanity checks when validating source-generation coverage.
- When changing dispatch logic, keep generated request/notification/behavior/validator registration aligned with `ISourceGeneratedMediator`.

