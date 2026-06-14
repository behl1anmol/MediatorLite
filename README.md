<div align="center">
  <img src="https://raw.githubusercontent.com/behl1anmol/MediatorLite/main/icon-readme.png" alt="MediatorLite Icon"/>
  <h1>MediatorLite</h1>
  <p>A lightweight, high-performance mediator library for .NET 10+.<br/>
  Built from the ground up with source generators for zero-reflection dispatch and minimal allocations.</p>

  <!-- CI & License -->
  [![CI](https://github.com/behl1anmol/MediatorLite/actions/workflows/ci.yml/badge.svg)](https://github.com/behl1anmol/MediatorLite/actions/workflows/ci.yml)
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)

  <!-- Packages -->
  [![MediatorLite](https://img.shields.io/nuget/v/MediatorLite.svg?label=MediatorLite)](https://www.nuget.org/packages/MediatorLite/)
  [![MediatorLite.SourceGeneration](https://img.shields.io/nuget/v/MediatorLite.SourceGeneration.svg?label=MediatorLite.SourceGeneration)](https://www.nuget.org/packages/MediatorLite.SourceGeneration/)
  [![MediatorLite.Abstractions](https://img.shields.io/nuget/v/MediatorLite.Abstractions.svg?label=MediatorLite.Abstractions)](https://www.nuget.org/packages/MediatorLite.Abstractions/)

  <!-- Downloads -->
  [![MediatorLite downloads](https://img.shields.io/nuget/dt/MediatorLite.svg?label=MediatorLite%20downloads)](https://www.nuget.org/packages/MediatorLite/)
  [![MediatorLite.SourceGeneration downloads](https://img.shields.io/nuget/dt/MediatorLite.SourceGeneration.svg?label=MediatorLite.SourceGeneration%20downloads)](https://www.nuget.org/packages/MediatorLite.SourceGeneration/)
  [![MediatorLite.Abstractions downloads](https://img.shields.io/nuget/dt/MediatorLite.Abstractions.svg?label=MediatorLite.Abstractions%20downloads)](https://www.nuget.org/packages/MediatorLite.Abstractions/)

  **[📚 Documentation](https://behl1anmol.github.io/MediatorLite)**
</div>

A lightweight, high-performance mediator library for .NET 10+. Built from the ground up with source generators for zero-reflection dispatch and minimal allocations.

> **Documentation:** [behl1anmol.github.io/MediatorLite](https://behl1anmol.github.io/MediatorLite)

## v2 Architecture

MediatorLite v2 is **source-generation-first**. The compile-time generated code provides:

- **O(1) dispatch** via generated switch expressions — no dictionary lookups or reflection
- **Compile-time attributes** control behavior ordering, notification strategies, and handler execution
- **Reflection fallback is deprecated** — manual DI registration still works but is no longer recommended

### Key Changes in v2

| Aspect | v1 | v2 |
|--------|----|----|
| **Primary dispatch** | Reflection with caching | Source-generated O(1) switch |
| **Behavior ordering** | DI registration order | `[BehaviorOrder]` attribute |
| **Notification strategies** | Runtime options | `[NotificationExecution]` / `[NotificationError]` attributes at compile-time (plus assembly-level defaults) |
| **Handler ordering** | `[NotificationHandlerOrder]` | `[NotificationHandlerOrder]` (unchanged) |
| **Reflection fallback** | Supported | Deprecated (still functional) |

## Features

- **O(1) Dispatch** - Source-generated switch expressions for constant-time handler resolution
- **Zero-Reflection Dispatch** - Compile-time handler discovery and typed dispatch via source generation
- **Compile-Time Configuration** - Attributes control behavior ordering, notification strategies, and more
- **Lightweight** - Minimal dependencies, focused core
- **Extensible** - Pipeline behaviors for cross-cutting concerns
- **Request/Response** - Type-safe command and query handling with `ValueTask`
- **Notifications** - Pub-sub pattern with sequential, parallel, and stop-on-first strategies
- **Observable** - Built-in logging and OpenTelemetry tracing support
- **DI Native** - First-class `Microsoft.Extensions.DependencyInjection` integration

## Installation

```bash
dotnet add package MediatorLite
dotnet add package MediatorLite.SourceGeneration   # Required for v2 source-generation-first architecture
```

Optional (contracts-only scenarios such as shared request assemblies):

```bash
dotnet add package MediatorLite.Abstractions
```

## Package and Versioning Guide

### Which package should I install?

| Project Type | Install | Why |
|--------------|---------|-----|
| Application/API (recommended) | `MediatorLite` + `MediatorLite.SourceGeneration` | Full runtime + compile-time O(1) dispatch |
| Application/API (legacy) | `MediatorLite` | Reflection fallback only (deprecated) |
| Shared contracts library (requests/notifications only) | `MediatorLite.Abstractions` | Keep shared package lightweight |

> ⚠️ **v2 Breaking Change:** Source generation is the **only** dispatch mechanism. Applications without `MediatorLite.SourceGeneration` cannot dispatch — `IMediator` throws an `InvalidOperationException` with setup guidance on first use.

### Will Abstractions be installed automatically?

- If you install `MediatorLite`, yes. `MediatorLite.Abstractions` is pulled transitively.
- If you install only `MediatorLite.SourceGeneration`, no. Source generation package does not pull runtime contracts by itself.
- If you install both `MediatorLite` and `MediatorLite.SourceGeneration`, yes (via `MediatorLite`).

### Compatibility Matrix

Use this matrix as the default rule for safe upgrades.

| MediatorLite.Abstractions | MediatorLite | MediatorLite.SourceGeneration | Supported | Notes |
|---------------------------|--------------|-------------------------------|-----------|-------|
| 1.0.x | 1.0.x | 1.0.x | Yes | Recommended lockstep |
| (transitive) | 1.0.x | 1.0.x | Yes | Typical app setup; Abstractions arrives via MediatorLite |
| 1.0.x | 1.0.x | not installed | Yes | Runtime works without source generation |
| 1.0.x | not installed | 1.0.x | No | Missing runtime package for mediator usage |
| 1.0.x | 2.0.x | 2.0.x | No | Cross-major mismatch with MediatorLite |
| 2.0.x | 2.0.x | 1.0.x | No | Source generator major mismatch |
| 1.1.x | 1.0.x | 1.0.x | Caution | May compile, but not a tested combination |

### Versioning Policy

- `MediatorLite.Abstractions` follows strict SemVer:
    - Patch: docs/internal fixes, no API break
    - Minor: additive API only, backward compatible
    - Major: breaking contract changes
- `MediatorLite` declares a dependency on `MediatorLite.Abstractions`.
- For predictable restores, keep all three packages on the same major and minor version.
- For pre-release builds, use matching pre-release versions across packages (for example, all `1.2.0-preview.3`).

### Upgrade Checklist

1. Upgrade `MediatorLite` first.
2. Upgrade `MediatorLite.SourceGeneration` to the same major/minor.
3. If you directly reference `MediatorLite.Abstractions`, align it to the same major/minor.
4. Run `dotnet restore` and `dotnet build` to force source regeneration and verify compatibility.

## Quick Start

### 1. Define a Request and Handler

```csharp
public record GetUserQuery(int Id) : IRequest<User>;

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, User>
{
    public ValueTask<User> HandleAsync(GetUserQuery request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new User(request.Id, "John Doe"));
    }
}
```

### 2. Register Services

The source generator automatically discovers all handlers at compile time. **You must call `AddGeneratedHandlers()` before `AddMediatorLite()`:**

```csharp
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // Registers handlers and the generated IMediator (typed dispatch)
    .AddMediatorLite();       // Optional diagnostic fallback; call order doesn't matter
```

`AddGeneratedHandlers()` registers:
- All `IRequestHandler<,>` implementations
- All `INotificationHandler<>` implementations
- All `IPipelineBehavior<,>` implementations (ordered by `[BehaviorOrder]`)
- The source-generated `IMediator` implementation (typed switch dispatch, no reflection, no boxing)

Built-in logging and tracing are on by default and emitted inline by the source generator. Opt out at compile time with assembly-level attributes:

```csharp
// In any .cs file in the consuming assembly:
[assembly: DisableMediatorLogging]   // Generator emits no logging calls
[assembly: DisableMediatorTracing]   // Generator emits no ActivitySource calls
```

The log **level** is controlled through `Microsoft.Extensions.Logging` configuration — generated code calls `LogDebug` under the `MediatorLite.IMediator` category, so filter it like any other logger:

```csharp
services.AddLogging(b => b.AddFilter("MediatorLite.IMediator", LogLevel.Information));
```

The generated mediator is registered as `Scoped` — it captures the resolving scope's `IServiceProvider`, so scoped handler dependencies resolve correctly. Handler lifetimes remain controlled by you at DI registration.

> ⚠️ **v2 Change:** `MediatorOptions`, the `AddMediatorLite(configure)` lambda, and the old `[NotificationOptions]` attribute have been **removed**. Notification execution/error strategies are compile-time only — use `[NotificationExecution]` / `[NotificationError]` on notification types, or `[assembly: DefaultNotificationExecution]` / `[assembly: DefaultNotificationError]` for an assembly-wide default.

#### Granular Registration

If you only need to register specific handler categories:

```csharp
services
    .AddGeneratedRequestHandlers()        // Only request handlers
    .AddGeneratedNotificationHandlers()   // Only notification handlers
    .AddGeneratedBehaviors()              // Only pipeline behaviors
    .AddMediatorLite();
```

#### Manual DI Registration (Not Supported)

> ⚠️ **Not supported in v2:** There is no reflection fallback. Without `AddGeneratedHandlers()`, the `IMediator` registered by `AddMediatorLite()` throws an `InvalidOperationException` with setup guidance on first use — manual handler registrations alone cannot be dispatched.

```csharp
services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserQueryHandler>();
services.AddTransient<INotificationHandler<UserCreatedNotification>, SendWelcomeEmailHandler>();
services.AddMediatorLite();  // Without AddGeneratedHandlers(), dispatch throws at first use
```

#### Excluding Types from Source Generation

Use `[MediatorGeneration(Skip = true)]` to exclude specific handlers from source-generated discovery:

```csharp
[MediatorGeneration(Skip = true)]
public class TestOnlyHandler : IRequestHandler<TestQuery, string>
{
    // This handler will NOT be registered by AddGeneratedHandlers()
}
```

### 3. Send Requests

```csharp
public class MyService(IMediator mediator)
{
    public async Task<User> GetUserAsync(int id, CancellationToken ct)
    {
        return await mediator.SendAsync(new GetUserQuery(id), ct);
    }
}
```

### 4. Publish Notifications

```csharp
public record UserCreatedNotification(int UserId, string Email) : INotification;

// Multiple handlers can subscribe
public class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification>
{
    public async ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken ct = default)
    {
        await _emailService.SendWelcomeAsync(notification.Email);
    }
}

// Publish
await mediator.PublishAsync(new UserCreatedNotification(user.Id, user.Email));
```

## Documentation

Full documentation is available at **[behl1anmol.github.io/MediatorLite](https://behl1anmol.github.io/MediatorLite)**.

- [Quick Start Guide](docs/quick-start.md)
- [Pipeline Behaviors](docs/pipeline-behaviors.md)
- [Validation](docs/validation.md)
- [Notifications](docs/notifications.md)
- [Migration from MediatR](docs/migration-from-mediatr.md)

## Notification Execution Strategies (v2)

In v2, notification strategies are controlled via **compile-time attributes**, not runtime options:

```csharp
// Apply strategy to a specific notification type
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record UserCreatedNotification(int UserId, string Email) : INotification;

// Or set an assembly-wide default once:
[assembly: DefaultNotificationExecution(NotificationExecutionStrategy.Parallel)]
[assembly: DefaultNotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
```

| Strategy | Description | Error Handling |
|----------|-------------|----------------|
| **Sequential** | Handlers run one-by-one in order | Error strategy applies |
| **Parallel** | All handlers run concurrently | Always aggregates exceptions* |
| **StopOnFirst** | Stops after first successful handler | Error strategy applies |

> *Parallel mode always aggregates exceptions because concurrent tasks cannot be cancelled mid-execution.

Control handler execution order with `[NotificationHandlerOrder]`:

```csharp
[NotificationHandlerOrder(1)]  // Executes first
public class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification> { }

[NotificationHandlerOrder(2)]  // Executes second
public class CreateAuditLogHandler : INotificationHandler<UserCreatedNotification> { }
```

See [Notifications documentation](docs/notifications.md) for detailed strategy behavior and error handling patterns.

## Why MediatorLite?

| Feature | MediatorLite v2 | MediatR |
|---------|----------------|---------|
| Handler Dispatch | O(1) source-generated switch | Reflection-based |
| Handler Discovery | Compile-time (source gen) | Runtime reflection |
| Behavior Ordering | `[BehaviorOrder]` attribute | Registration order |
| Notification Strategies | `[NotificationExecution]` / `[NotificationError]` attributes + assembly-level defaults | Runtime configuration |
| ValueTask Support | Native | Task only |
| OpenTelemetry | Built-in | Manual |
| Notification Strategies | Sequential, Parallel, StopOnFirst | Sequential only |
| License | MIT | Commercial |

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
