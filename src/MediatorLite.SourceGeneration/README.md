# MediatorLite.SourceGeneration

Source generators for MediatorLite that enable compile-time handler discovery with zero runtime reflection.

## What is this?

MediatorLite.SourceGeneration is a Roslyn source generator that automatically discovers and registers handlers, behaviors, and validators at compile-time. This eliminates runtime reflection overhead and provides faster startup times and better performance.

## Why use Source Generation?

- **Zero Runtime Reflection**: Handler discovery happens at compile-time, not runtime
- **Faster Startup**: No need to scan assemblies for handlers during application initialization
- **Better Performance**: Direct method calls instead of reflection-based invocation
- **Compile-Time Safety**: Errors are caught during compilation, not at runtime
- **Trimming-Friendly**: Works with .NET's native AOT and assembly trimming

## Installation

Install both packages together:

```bash
dotnet add package MediatorLite
dotnet add package MediatorLite.SourceGeneration
```

## Usage

### 1. Register Services with Source Generation

The source generator automatically discovers all handlers, behaviors, and validators at compile time:

```csharp
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // Registers all handlers, behaviors, validators
    .AddMediatorLite();       // Registers the mediator
```

That's it! The source generator:
- Discovers all `IRequestHandler<,>` implementations
- Discovers all `INotificationHandler<>` implementations
- Discovers all `IPipelineBehavior<,>` implementations
- Discovers all `IValidator<TRequest>` implementations
- Auto-registers `DataAnnotationsValidator<T>` for types with validation attributes
- Registers everything with the DI container

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

### Configurable Handler Execution

Use attributes to control notification handler behavior:

```csharp
// Control handler execution order
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

Override global notification execution strategy per notification:

```csharp
[NotificationOptions(
    ExecutionStrategy = NotificationExecutionStrategy.Parallel,
    ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate)]
public record UserCreatedNotification(int UserId) : INotification;
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

### Pipeline Behaviors

Create behaviors for cross-cutting concerns:

```csharp
[BehaviorOrder(1)]  // Execute first (optional)
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

// Automatically discovered and registered by AddGeneratedHandlers()
```

## Performance

Source generation provides significant performance improvements:

- **Faster handler resolution** - Direct type mapping instead of reflection scanning
- **Faster startup** - No assembly scanning during application initialization
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
4. Generates type-safe registration code
5. Integrates seamlessly with MediatorLite's runtime

All of this happens during build - no runtime scanning required!

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

## Manual Registration (Without Source Generation)

If you prefer manual registration, you can skip the source generator package:

```csharp
// Install only MediatorLite package
dotnet add package MediatorLite

// Register manually
services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserQueryHandler>();
services.AddTransient<INotificationHandler<UserCreated>, SendEmailHandler>();
services.AddMediatorLite();
```

## Source Code

Visit the [MediatorLite repository](https://github.com/behl1anmol/MediatorLite) for:
- Full documentation
- Source code
- Examples and samples
- Issue tracking

## License

This package is part of MediatorLite and shares the same MIT license.
