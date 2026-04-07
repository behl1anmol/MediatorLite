# MediatorLite Agent Guide

- Start with `.github/copilot-instructions.md`; use this file as the short repo map.
- Role-specific guidance lives in `.github/agents/*.agent.md` (`code-reviewer`, `dotnet-self-learning-architect`).

## Big picture
- Core dispatch is in `src/MediatorLite/Internal/Mediator.cs`.
- `SendAsync<T>` routes one `IRequest<T>` to a single `IRequestHandler<,>`; `PublishAsync` fans out `INotification` to all matching handlers.
- There are two supported wiring modes:
  - Source-generated: call `AddGeneratedHandlers()` then `AddMediatorLite()` for zero-reflection dispatch.
  - Manual DI: register handlers/behaviors yourself, then call `AddMediatorLite()`; `Mediator.cs` falls back to reflection caches when `ISourceGeneratedMediator` is absent.

## Important patterns
- DI registration lives in `src/MediatorLite/Configuration/ServiceCollectionExtensions.cs`.
- Runtime knobs are in `src/MediatorLite/Configuration/MediatorOptions.cs`.
- Public attributes are in `src/MediatorLite.Abstractions/Abstractions/Attributes.cs`.
- Source-generation entry point is `src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs`; generated diagnostics surface as `MediatorLite.Generated.MediatorLiteRegistration`.
- Behaviors execute in DI registration order; `BehaviorOrderAttribute` exists, but tests show ordering currently follows registration (`tests/MediatorLite.Tests/Reflection/PipelineBehaviorTests.cs`).
- Behaviors may short-circuit by not calling `next()`.
- Notifications honor `NotificationHandlerOrderAttribute` and per-notification `NotificationOptionsAttribute`.

## Validation and observability
- Validation lives in `src/MediatorLite/Validation/Validation.cs`; source-gen samples auto-register `ValidationBehavior<,>` and `DataAnnotationsValidator<T>`.
- Built-in logging and tracing are controlled by `MediatorOptions.EnableBuiltInLogging` and `EnableTracing`.
- Observability tags and OpenTelemetry setup are documented in `docs/observability.md`.

## Project conventions
- Target framework is `net10.0`; nullable and implicit usings are enabled; warnings are treated as errors (`Directory.Build.props`).
- Public APIs use `Task`/`Task<T>` for the mediator surface, but handlers and behaviors use `ValueTask`.
- Use `IRequest<Unit>` for commands with no response.
- `[MediatorGeneration(Skip = true)]` is used in tests and manual behaviors/handlers that should not be source-generated.

## Workflows
- Build/test with `dotnet test MediatorLite.sln`.
- Check `samples/MediatorLite.Sample/Program.cs` for manual DI registration and `samples/MediatorLite.Sample.SourceGen/Program.cs` for the full source-generated path.
- Test layout: `tests/MediatorLite.Tests/{Reflection,SourceGeneration,UnitTests}`.
- When changing dispatch logic, keep reflection fallback and source-generated behavior aligned.

