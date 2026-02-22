<p align="center">
    <img src="icon.png" alt="MediatorLite Icon" width="350" /> 
</p>

[![CI](https://github.com/behl1anmol/MediatorLite/actions/workflows/ci.yml/badge.svg)](https://github.com/behl1anmol/MediatorLite/actions/workflows/ci.yml)
[![MediatorLite Version](https://img.shields.io/nuget/v/MediatorLite.svg?label=MediatorLite)](https://www.nuget.org/packages/MediatorLite/)
[![MediatorLite Downloads](https://img.shields.io/nuget/dt/MediatorLite.svg?label=MediatorLite%20downloads)](https://www.nuget.org/packages/MediatorLite/)
[![MediatorLite.SourceGeneration Version](https://img.shields.io/nuget/v/MediatorLite.SourceGeneration.svg?label=MediatorLite.SourceGeneration)](https://www.nuget.org/packages/MediatorLite.SourceGeneration/)
[![MediatorLite.SourceGeneration Downloads](https://img.shields.io/nuget/dt/MediatorLite.SourceGeneration.svg?label=MediatorLite.SourceGeneration%20downloads)](https://www.nuget.org/packages/MediatorLite.SourceGeneration/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)

A lightweight, high-performance mediator library for .NET 10+. Built from the ground up with source generators for zero-reflection dispatch and minimal allocations.

## Features

- **High Performance** - Source generators eliminate runtime reflection for handler dispatch
- **Zero-Reflection Dispatch** - Compile-time handler discovery and typed dispatch via source generation
- **Lightweight** - Minimal dependencies, focused core
- **Extensible** - Pipeline behaviors for cross-cutting concerns
- **Request/Response** - Type-safe command and query handling with `ValueTask`
- **Notifications** - Pub-sub pattern with sequential, parallel, and stop-on-first strategies
- **Observable** - Built-in logging and OpenTelemetry tracing support
- **DI Native** - First-class `Microsoft.Extensions.DependencyInjection` integration

## Installation

```bash
dotnet add package MediatorLite
```

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

The source generator automatically discovers all handlers at compile time. Use `AddGeneratedHandlers()` to register them with the DI container:

```csharp
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // Source-generated: registers all handlers, notifications, behaviors
    .AddMediatorLite();       // Registers the mediator and options
```

`AddGeneratedHandlers()` registers:
- All `IRequestHandler<,>` implementations
- All `INotificationHandler<>` implementations
- All `IPipelineBehavior<,>` implementations
- The `ISourceGeneratedMediator` for zero-reflection dispatch

To configure options:

```csharp
services
    .AddGeneratedHandlers()
    .AddMediatorLite(options =>
    {
        options.EnableBuiltInLogging = true;
        options.EnableTracing = true;
        options.NotificationExecutionStrategy = NotificationExecutionStrategy.Parallel;
    });
```

#### Granular Registration

If you only need to register specific handler categories:

```csharp
services
    .AddGeneratedRequestHandlers()        // Only request handlers
    .AddGeneratedNotificationHandlers()   // Only notification handlers
    .AddGeneratedBehaviors()              // Only pipeline behaviors
    .AddMediatorLite();
```

#### Manual DI Registration (Without Source Generation)

You can register handlers manually with standard DI if preferred:

```csharp
services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserQueryHandler>();
services.AddTransient<INotificationHandler<UserCreatedNotification>, SendWelcomeEmailHandler>();
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddMediatorLite();
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

- [Quick Start Guide](docs/quick-start.md)
- [Pipeline Behaviors](docs/pipeline-behaviors.md)
- [Validation](docs/validation.md)
- [Notifications](docs/notifications.md)
- [Migration from MediatR](docs/migration-from-mediatr.md)

## Notification Execution Strategies

MediatorLite provides flexible notification execution with three strategies:

| Strategy | Description | Error Handling |
|----------|-------------|----------------|
| **Sequential** | Handlers run one-by-one in order | Error strategy applies |
| **Parallel** | All handlers run concurrently | Always aggregates exceptions* |
| **StopOnFirst** | Stops after first successful handler | Error strategy applies |

> *Parallel mode always aggregates exceptions because concurrent tasks cannot be cancelled mid-execution.

```csharp
services.AddMediatorLite(options =>
{
    options.NotificationExecutionStrategy = NotificationExecutionStrategy.Parallel;
    options.NotificationErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate;
});
```

See [Notifications documentation](docs/notifications.md) for detailed strategy behavior and error handling patterns.

## Why MediatorLite?

| Feature | MediatorLite | MediatR |
|---------|-------------|---------|
| Handler Discovery | Compile-time (source gen) | Runtime reflection |
| Handler Dispatch | Zero-reflection typed dispatch | Reflection-based |
| ValueTask Support | Native | Task only |
| OpenTelemetry | Built-in | Manual |
| Notification Strategies | Sequential, Parallel, StopOnFirst | Sequential only |
| License | MIT | Commercial |

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
