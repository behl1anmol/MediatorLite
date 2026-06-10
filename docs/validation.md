# Validation

MediatorLite includes built-in validation support through pipeline behaviors. This allows you to validate requests before they reach your handlers.

## Overview

The validation system provides:
- **`ValidationBehavior<TRequest, TResponse>`** - A pipeline behavior that validates requests
- **`IValidator<TRequest>`** - Interface for creating custom validators
- **`DataAnnotationsValidator<TRequest>`** - Built-in validator using `System.ComponentModel.DataAnnotations`
- **`ValidationException`** - Exception thrown when validation fails
- **`ValidationResult`** - Result object containing validation errors

## Quick Start

### 1. Define a Request with Validation Attributes

```csharp
using System.ComponentModel.DataAnnotations;

public record CreateUserCommand : IRequest<int>
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters")]
    public required string Name { get; init; }

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public required string Email { get; init; }

    [Range(18, 120, ErrorMessage = "Age must be between 18 and 120")]
    public int Age { get; init; }
}
```

### 2. Register Services

#### With Source Generation (Required for v2)

The source generator automatically handles validation setup:

- **Discovers** all `IValidator<T>` implementations at compile time
- **Auto-registers** `DataAnnotationsValidator<T>` for request types with DataAnnotation attributes
- **Registers** `ValidationBehavior<,>` respecting `[BehaviorOrder]` attributes

```csharp
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // MUST be called first — registers handlers, validators, behaviors with O(1) dispatch
    .AddMediatorLite();

// That's it! No manual validator or behavior registration needed.
// The source generator:
//   1. Detects [Required], [Range], etc. on CreateUserCommand properties
//   2. Registers DataAnnotationsValidator<CreateUserCommand> automatically
//   3. Registers ValidationBehavior<CreateUserCommand, int> with proper [BehaviorOrder]
//   4. Discovers any custom IValidator<CreateUserCommand> implementations
```

For granular control, use the individual methods:

```csharp
services
    .AddGeneratedRequestHandlers()
    .AddGeneratedNotificationHandlers()
    .AddGeneratedValidators()       // Registers discovered validators + DataAnnotationsValidator
    .AddGeneratedBehaviors()        // Registers behaviors with [BehaviorOrder] ordering
    .AddMediatorLite();
```

#### Without Source Generation (Not Supported)

> ⚠️ **Not supported in v2:** There is no reflection fallback. Manual registrations of `ValidationBehavior<,>` or validators are never dispatched without `AddGeneratedHandlers()` — the `IMediator` registered by `AddMediatorLite()` alone throws on first use. Hand-registering validators alongside source generation is also a smell: the generator already registered them via `AddGeneratedValidators()`, and duplicates inflate `ValidatorCount`.

### 3. Handle ValidationException

```csharp
try
{
    var userId = await mediator.SendAsync(new CreateUserCommand
    {
        Name = "",  // Invalid: too short
        Email = "invalid-email",  // Invalid: bad format
        Age = 15  // Invalid: below minimum
    });
}
catch (ValidationException ex)
{
    foreach (var error in ex.Errors)
    {
        Console.WriteLine($"{error.PropertyName}: {error.ErrorMessage}");
    }
    // Output:
    // Name: Name must be between 2 and 100 characters
    // Email: Invalid email format
    // Age: Age must be between 18 and 120
}
```

## Custom Validators

Create custom validators by implementing `IValidator<TRequest>`. The source generator automatically discovers and registers these at compile time:

```csharp
public class CreateUserCommandValidator : IValidator<CreateUserCommand>
{
    private readonly IUserRepository _userRepository;

    public CreateUserCommandValidator(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async ValueTask<ValidationResult> ValidateAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();

        // Check if email already exists (async validation)
        if (await _userRepository.EmailExistsAsync(request.Email, cancellationToken))
        {
            errors.Add(new ValidationError(
                nameof(request.Email),
                "Email is already registered",
                request.Email));
        }

        // Custom business logic validation
        if (request.Age < 18 && !request.HasParentalConsent)
        {
            errors.Add(new ValidationError(
                nameof(request.Age),
                "Users under 18 require parental consent"));
        }

        return errors.Count > 0
            ? ValidationResult.Failure(errors)
            : ValidationResult.Success;
    }
}
```

Register the custom validator (only needed without source generation):

```csharp
// With source generation: automatically discovered and registered by AddGeneratedHandlers()
// Without source generation: register manually
services.AddTransient<IValidator<CreateUserCommand>, CreateUserCommandValidator>();
```

Use `[MediatorGeneration(Skip = true)]` to exclude a validator from source generation:

```csharp
[MediatorGeneration(Skip = true)]
public class TestOnlyValidator : IValidator<CreateUserCommand>
{
    // This validator will NOT be registered by AddGeneratedHandlers()
}
```

## Multiple Validators

You can register multiple validators for the same request. `ValidationBehavior` will execute all of them and aggregate errors:

```csharp
// Register both DataAnnotations and custom validator
services.AddTransient<IValidator<CreateUserCommand>, DataAnnotationsValidator<CreateUserCommand>>();
services.AddTransient<IValidator<CreateUserCommand>, CreateUserCommandValidator>();
```

When validation fails, all errors from all validators are collected and thrown in a single `ValidationException`.

## Validation Error Details

`ValidationError` contains:

```csharp
public sealed record ValidationError(
    string PropertyName,        // Property that failed validation
    string ErrorMessage,        // Error description
    object? AttemptedValue);    // The value that failed (optional)
```

Example usage:

```csharp
catch (ValidationException ex)
{
    var errorDetails = ex.Errors.Select(e => new
    {
        Field = e.PropertyName,
        Message = e.ErrorMessage,
        Value = e.AttemptedValue
    });

    return Results.ValidationProblem(
        errorDetails.ToDictionary(
            e => e.Field,
            e => new[] { e.Message }));
}
```

## DataAnnotations Support

The built-in `DataAnnotationsValidator<T>` supports all standard `System.ComponentModel.DataAnnotations` attributes:

| Attribute | Description |
|-----------|-------------|
| `[Required]` | Property must have a value |
| `[StringLength]` | String length constraints |
| `[Range]` | Numeric range validation |
| `[EmailAddress]` | Email format validation |
| `[Phone]` | Phone number format |
| `[Url]` | URL format validation |
| `[RegularExpression]` | Custom regex pattern |
| `[Compare]` | Compare two properties |
| `[CreditCard]` | Credit card format |
| Custom attributes | Any attribute inheriting `ValidationAttribute` |

## Validation Behavior Execution Order (v2)

`ValidationBehavior` runs as part of the pipeline. Use `[BehaviorOrder]` to ensure it executes first:

### With Source Generation (Automatic)

The source generator respects `[BehaviorOrder]` attributes. Add `[BehaviorOrder(0)]` to your `ValidationBehavior` to ensure it runs first:

```csharp
[BehaviorOrder(0)]  // Runs before other behaviors
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }
```

```
Request → [BehaviorOrder(0)] ValidationBehavior → [BehaviorOrder(1)] LoggingBehavior → Handler
```

If validation fails, the pipeline short-circuits and subsequent behaviors/handlers are not executed.

### Without Source Generation (Deprecated)

> ⚠️ **Deprecated in v2:** Manual registration order is deprecated. Use `[BehaviorOrder]` with source generation.

Register `ValidationBehavior` before other behaviors to ensure it runs first:

```csharp
// ValidationBehavior runs first (validates before logging)
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

## Source Generator Diagnostics

The source generator exposes validator counts for diagnostics:

```csharp
using MediatorLite.Generated;

Console.WriteLine($"Validators discovered: {MediatorLiteRegistration.ValidatorCount}");
```

## Advanced Patterns

### Conditional Validation

```csharp
public class ConditionalValidator : IValidator<MyCommand>
{
    public ValueTask<ValidationResult> ValidateAsync(MyCommand request, CancellationToken ct = default)
    {
        // Skip validation for admin users
        if (request.IsAdminUser)
        {
            return ValueTask.FromResult(ValidationResult.Success);
        }

        // Validate for normal users
        var errors = new List<ValidationError>();
        // ... validation logic

        return ValueTask.FromResult(
            errors.Count > 0
                ? ValidationResult.Failure(errors)
                : ValidationResult.Success);
    }
}
```

### Fluent Validation Integration

You can integrate FluentValidation by adapting its validators to `IValidator<T>`:

```csharp
public class FluentValidationAdapter<TRequest> : IValidator<TRequest>
{
    private readonly FluentValidation.IValidator<TRequest> _fluentValidator;

    public FluentValidationAdapter(FluentValidation.IValidator<TRequest> fluentValidator)
    {
        _fluentValidator = fluentValidator;
    }

    public async ValueTask<ValidationResult> ValidateAsync(
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _fluentValidator.ValidateAsync(request, cancellationToken);

        if (result.IsValid)
        {
            return ValidationResult.Success;
        }

        var errors = result.Errors.Select(e => new ValidationError(
            e.PropertyName,
            e.ErrorMessage,
            e.AttemptedValue)).ToList();

        return ValidationResult.Failure(errors);
    }
}
```

Register:

```csharp
services.AddValidatorsFromAssemblyContaining<Program>();  // FluentValidation
services.AddTransient(typeof(IValidator<>), typeof(FluentValidationAdapter<>));
```

### Per-Request Validator Registration

With source generation, validation is automatically scoped to request types that have validators or DataAnnotation attributes. Request types without either are not validated:

```csharp
// CreateUserCommand has [Required], [EmailAddress], etc.
// Source generator auto-registers DataAnnotationsValidator<CreateUserCommand>

// UpdateUserCommand has no annotations and no custom validator
// No validation is registered - ValidationBehavior is not added for this type
```

Without source generation, register validators per-request manually:

```csharp
// Only CreateUserCommand has validation
services.AddTransient<IValidator<CreateUserCommand>, DataAnnotationsValidator<CreateUserCommand>>();

// UpdateUserCommand - no validators registered, validation is skipped
// ValidationBehavior will call next() immediately when no validators are found
```

## Testing Validators

```csharp
[Fact]
public async Task Validator_ShouldFailForInvalidEmail()
{
    var validator = new DataAnnotationsValidator<CreateUserCommand>();

    var command = new CreateUserCommand
    {
        Name = "John Doe",
        Email = "invalid-email",  // Invalid
        Age = 25
    };

    var result = await validator.ValidateAsync(command);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().ContainSingle(e =>
        e.PropertyName == nameof(CreateUserCommand.Email) &&
        e.ErrorMessage.Contains("email"));
}
```

## Best Practices

1. **Use `AddGeneratedHandlers()` first** — Required for v2 O(1) dispatch and proper `[BehaviorOrder]` support
2. **Use `[BehaviorOrder(0)]` for ValidationBehavior** — Ensures validation runs first
3. **Use DataAnnotations for simple validation** — Required, ranges, string lengths, formats
4. **Use custom validators for business logic** — Async database checks, complex rules
5. **Create specific error messages** — Help users understand what went wrong
6. **Include AttemptedValue in errors** — Useful for logging and debugging
7. **Handle ValidationException at API boundary** — Convert to HTTP 400 responses
8. **Use `[MediatorGeneration(Skip = true)]`** — To exclude test-only validators from source generation

## See Also

- [Pipeline Behaviors](pipeline-behaviors.md) - Learn about behavior execution order
- [Quick Start](quick-start.md) - Basic MediatorLite setup
