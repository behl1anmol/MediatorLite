# ![MediatorLite Icon](https://raw.githubusercontent.com/behl1anmol/MediatorLite/main/icon-readme.png) MediatorLite.SourceGeneration

[![CI](https://github.com/behl1anmol/MediatorLite/actions/workflows/ci.yml/badge.svg)](https://github.com/behl1anmol/MediatorLite/actions/workflows/ci.yml)
[![MediatorLite Version](https://img.shields.io/nuget/v/MediatorLite.svg?label=MediatorLite)](https://www.nuget.org/packages/MediatorLite/)
[![MediatorLite Downloads](https://img.shields.io/nuget/dt/MediatorLite.svg?label=MediatorLite%20downloads)](https://www.nuget.org/packages/MediatorLite/)
[![MediatorLite.SourceGeneration Version](https://img.shields.io/nuget/v/MediatorLite.SourceGeneration.svg?label=MediatorLite.SourceGeneration)](https://www.nuget.org/packages/MediatorLite.SourceGeneration/)
[![MediatorLite.SourceGeneration Downloads](https://img.shields.io/nuget/dt/MediatorLite.SourceGeneration.svg?label=MediatorLite.SourceGeneration%20downloads)](https://www.nuget.org/packages/MediatorLite.SourceGeneration/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)

Source generators for MediatorLite v2 that enable **O(1) dispatch** via compile-time generated switch expressions.

> **Documentation:** [behl1anmol.github.io/MediatorLite](https://behl1anmol.github.io/MediatorLite)

## What is this?

MediatorLite.SourceGeneration is a Roslyn source generator that:
- Generates **O(1) switch expressions** for handler dispatch (no dictionary lookups or reflection)
- Respects **`[BehaviorOrder]`** attributes for pipeline behavior ordering
- Respects **`[NotificationExecution]`** / **`[NotificationError]`** attributes for per-notification strategies, merged with **`[assembly: DefaultNotificationExecution]`** / **`[assembly: DefaultNotificationError]`** defaults
- Respects **`[NotificationHandlerOrder]`** attributes for handler execution order

## v2 Architecture

MediatorLite v2 is **source-generation-first**. This package is **required** for the v2 architecture:

| Aspect | Without Source Gen (Deprecated) | With Source Gen (v2) |
|--------|--------------------------------|----------------------|
| Handler dispatch | Reflection with caching | O(1) generated switch |
| Behavior ordering | DI registration order | `[BehaviorOrder]` attribute |
| Notification strategies | Not supported | `[NotificationExecution]` / `[NotificationError]` + `[assembly: Default...]` defaults |
| Performance | Slower (dictionary + reflection) | Faster (direct method calls) |

## Why use Source Generation?

- **O(1) Dispatch**: Generated switch expressions provide constant-time handler resolution
- **Compile-Time Configuration**: `[BehaviorOrder]`, `[NotificationExecution]`, `[NotificationError]`, and `[NotificationHandlerOrder]` attributes control behavior (plus assembly-level defaults for notification strategies)
- **Zero Runtime Reflection**: Handler discovery happens at compile-time, not runtime
- **Faster Startup**: No need to scan assemblies for handlers during application initialization
- **Better Performance**: Direct method calls instead of reflection-based invocation
- **Compile-Time Safety**: Errors are caught during compilation, not at runtime
- **Trimming-Friendly**: Works with .NET's native AOT and assembly trimming

## Installation

Install both packages together (required for v2):

```bash
dotnet add package MediatorLite
dotnet add package MediatorLite.SourceGeneration   # Required for O(1 dispatch
```

## Usage

### 1. Register Services with Source Generation

**You must call `AddGeneratedHandlers()` before `AddMediatorLite()`:**

```csharp
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // MUST be called first — registers handlers and O(1) dispatch
    .AddMediatorLite();       // Registers the mediator runtime
```

That's it! The source generator:
- Discovers all `IRequestHandler<,>` implementations
- Discovers all `INotificationHandler<>` implementations
- Discovers all `IPipelineBehavior<,>` implementations (ordered by `[BehaviorOrder]`)
- Discovers all `IValidator<TRequest>` implementations
- Auto-registers `DataAnnotationsValidator<T>` for types with validation attributes
- Registers everything with the DI container

### Observability (on by default, compile-time opt-out)

The generator emits `ILogger` calls and `ActivitySource` events inline into every generated `Pipeline_*` and `Publish_*` method. Both are **on by default**. Opt out at compile time with assembly-level attributes (both no-arg, in the `MediatorLite` namespace):

```csharp
[assembly: DisableMediatorLogging]   // Generator emits no logging calls
[assembly: DisableMediatorTracing]   // Generator emits no ActivitySource calls
```

When a `Disable*` attribute is absent the generator emits the corresponding calls inline; when it is present those calls are skipped entirely (no branch-free runtime check, no dead code).

The log **level** is controlled through standard `Microsoft.Extensions.Logging` configuration. Generated code always calls `LogDebug` under the `MediatorLite.IMediator` category:

```csharp
services.AddLogging(b => b.AddFilter("MediatorLite.IMediator", LogLevel.Information));
```

> Notification execution/error strategies are controlled at compile time via `[NotificationExecution]` / `[NotificationError]` on the notification type, or assembly-level `[assembly: DefaultNotificationExecution]` / `[assembly: DefaultNotificationError]`.

### 2. Define Handlers

The source generator automatically discovers handlers - no attributes needed:

```csharp
// Request handler - automatically discovered
public class GetUserQueryHandler : IRequestHandler<GetUserQuery, User>
{
    public async ValueTask<User> HandleAsync(
        GetUserQuery request,
        CancellationToken cancellationToken = default)
    {
        // Your logic here
    }
}

// Notification handler - automatically discovered
public class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification>
{
    public async ValueTask HandleAsync(
        UserCreatedNotification notification,
        CancellationToken cancellationToken = default)
    {
        // Your logic here
    }
}
```

### 3. Build Your Project

The source generator runs during compilation and generates registration code automatically. You'll see the generated code in your IDE (under Dependencies → Analyzers → MediatorLite.SourceGeneration).

## Features

### Granular Registration

If you only need specific handler categories:

```csharp
services
    .AddGeneratedRequestHandlers()        // Only request handlers
    .AddGeneratedNotificationHandlers()   // Only notification handlers
    .AddGeneratedBehaviors()              // Only pipeline behaviors
    .AddGeneratedValidators()             // Only validators
    .AddMediatorLite();
```

### Excluding Types from Source Generation

Use `[MediatorGeneration(Skip = true)]` to exclude specific handlers:

```csharp
[MediatorGeneration(Skip = true)]
public class TestOnlyHandler : IRequestHandler<TestQuery, string>
{
    // This handler will NOT be registered by AddGeneratedHandlers()
}
```

### Configurable Handler Execution (v2 Attributes)

Use compile-time attributes to control behavior:

**Behavior ordering with `[BehaviorOrder]`:**

```csharp
[BehaviorOrder(1)]  // Executes first
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling {RequestType}", typeof(TRequest).Name);
        var response = await next();
        _logger.LogInformation("Handled {RequestType}", typeof(TRequest).Name);
        return response;
    }
}

[BehaviorOrder(2)]  // Executes second
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }
```

**Notification strategies with `[NotificationExecution]` / `[NotificationError]`:**

```csharp
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record UserCreatedNotification(int UserId) : INotification;
```

**Assembly-wide defaults:**

```csharp
[assembly: DefaultNotificationExecution(NotificationExecutionStrategy.Parallel)]
[assembly: DefaultNotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
```

Per-notification attributes win over assembly-level defaults. If neither is set, the library defaults (`Sequential` + `StopOnFirstError`) apply.

> ⚠️ **v2 Hard Break:** `MediatorOptions` is gone, `AddMediatorLite` no longer accepts a configure lambda, and the old runtime notification-strategy properties, the `[NotificationOptions]` attribute, and `ISourceGeneratedMediator.GetNotificationOptions` have been **removed**. Strategies are resolved at compile time and baked into each `Publish_*` method as a single branch-free code path.

**Handler ordering with `[NotificationHandlerOrder]`:**

```csharp
[NotificationHandlerOrder(1)]  // Execute first
public class FirstHandler : INotificationHandler<UserCreated>
{
    public async ValueTask HandleAsync(UserCreated notification, CancellationToken ct = default)
    {
        // Executes first
    }
}

[NotificationHandlerOrder(2)]  // Execute second
public class SecondHandler : INotificationHandler<UserCreated>
{
    public async ValueTask HandleAsync(UserCreated notification, CancellationToken ct = default)
    {
        // Executes second
    }
}
```

### Automatic Validation Support

The source generator automatically handles validation:

```csharp
using System.ComponentModel.DataAnnotations;

public record CreateUserCommand : IRequest<int>
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, MinimumLength = 2)]
    public required string Name { get; init; }

    [Required]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public required string Email { get; init; }
}

// Source generator automatically:
// 1. Detects DataAnnotation attributes on CreateUserCommand
// 2. Registers DataAnnotationsValidator<CreateUserCommand>
// 3. Registers ValidationBehavior<CreateUserCommand, int> first in pipeline
```

Create custom validators:

```csharp
public class CreateUserValidator : IValidator<CreateUserCommand>
{
    public async ValueTask<ValidationResult> ValidateAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();

        // Custom validation logic
        if (await _userRepository.EmailExistsAsync(request.Email, cancellationToken))
        {
            errors.Add(new ValidationError(
                nameof(request.Email),
                "Email is already registered",
                request.Email));
        }

        return errors.Count > 0
            ? ValidationResult.Failure(errors)
            : ValidationResult.Success;
    }
}

// Automatically discovered and registered by AddGeneratedHandlers()
```

### Pipeline Behaviors (v2)

Create behaviors for cross-cutting concerns with `[BehaviorOrder]`:

```csharp
[BehaviorOrder(1)]  // Execute first
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling {RequestType}", typeof(TRequest).Name);
        var response = await next();
        _logger.LogInformation("Handled {RequestType}", typeof(TRequest).Name);
        return response;
    }
}

// Automatically discovered and registered by AddGeneratedHandlers() with proper ordering
```

> ⚠️ **v2 Change:** Behavior execution order is determined by `[BehaviorOrder]`, not DI registration order.

## Performance

Source generation provides significant performance improvements:

- **O(1) handler resolution** — Generated switch expressions instead of dictionary lookups
- **No reflection** — Direct typed method calls instead of `MethodInfo.Invoke()`
- **Faster startup** — No assembly scanning during application initialization
- **Zero allocation** for handler lookups with source-generated mediator
- **Native AOT compatible** for maximum performance

## Diagnostics

The source generator exposes counts for diagnostics:

```csharp
using MediatorLite.Generated;

Console.WriteLine($"Request handlers: {MediatorLiteRegistration.RequestHandlerCount}");
Console.WriteLine($"Notification handlers: {MediatorLiteRegistration.NotificationHandlerCount}");
Console.WriteLine($"Behaviors: {MediatorLiteRegistration.BehaviorCount}");
Console.WriteLine($"Validators: {MediatorLiteRegistration.ValidatorCount}");
```

## Requirements

- .NET Standard 2.0+ (for the generator itself)
- MediatorLite package (same version)
- C# 9.0 or later recommended for best IDE experience

## How It Works

The source generator:

1. Scans your compilation for handler implementations during build
2. Generates `MediatorLiteRegistration` class with extension methods
3. Creates `AddGeneratedHandlers()`, `AddGeneratedRequestHandlers()`, etc.
4. Generates **O(1 switch expressions** for handler dispatch
5. Reads `[BehaviorOrder]` attributes and generates ordered behavior chains
6. Reads `[NotificationExecution]` / `[NotificationError]` attributes (and `[assembly: Default...]` defaults) and bakes the resolved strategy for each notification directly into its `Publish_*` body
7. Reads `[NotificationHandlerOrder]` attributes and generates ordered handler execution

All of this happens during build — no runtime scanning or reflection required!

## Troubleshooting

### Generator Not Running

If handlers aren't being discovered:

1. Ensure both `MediatorLite` and `MediatorLite.SourceGeneration` packages are installed
2. Clean and rebuild your solution (`dotnet clean && dotnet build`)
3. Check that you're using `AddGeneratedHandlers()` from `MediatorLite.Generated` namespace
4. Verify your handlers are in the same project or referenced projects

### IDE Not Showing Generated Code

- Restart your IDE after installing the package
- In Visual Studio: Check Solution Explorer → Dependencies → Analyzers → MediatorLite.SourceGeneration
- In Rider: Check Solution → Dependencies → Source Generators

### Build Warnings

The generator may emit warnings for:
- Duplicate handler registrations
- Missing dependencies
- Invalid attribute usage

These are informational and help catch configuration issues early.

## Manual Registration (Not Supported)

Manual registration without the source generator is **not supported in v2** — there is no reflection fallback:

```csharp
// Install only MediatorLite package (unsupported path)
dotnet add package MediatorLite

services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserQueryHandler>();
services.AddTransient<INotificationHandler<UserCreated>, SendEmailHandler>();
services.AddMediatorLite();  // Without AddGeneratedHandlers(), dispatch throws at first use
```

> ⚠️ The `IMediator` registered by `AddMediatorLite()` alone is a diagnostic fallback that throws an `InvalidOperationException` with setup guidance. Reference the `MediatorLite.SourceGeneration` package and call `AddGeneratedHandlers()`.

## Source Code

Full documentation is available at **[behl1anmol.github.io/MediatorLite](https://behl1anmol.github.io/MediatorLite)**.

Visit the [MediatorLite repository](https://github.com/behl1anmol/MediatorLite) for:
- Full documentation
- Source code
- Examples and samples
- Issue tracking

## License

This package is part of MediatorLite and shares the same MIT license.
