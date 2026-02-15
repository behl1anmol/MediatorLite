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

### 2. Register ValidationBehavior and Validators

#### With Source Generation

```csharp
using MediatorLite.Generated;
using MediatorLite.Validation;

services
    .AddGeneratedHandlers()   // Registers handlers
    .AddMediatorLite();

// Register the validation behavior as an open generic
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// Register DataAnnotations validator for your request
services.AddTransient<IValidator<CreateUserCommand>, DataAnnotationsValidator<CreateUserCommand>>();
```

#### Without Source Generation

```csharp
using MediatorLite.Validation;

services.AddMediatorLite(options =>
{
    options.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

services.AddTransient<IValidator<CreateUserCommand>, DataAnnotationsValidator<CreateUserCommand>>();
```

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

Create custom validators by implementing `IValidator<TRequest>`:

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

Register the custom validator:

```csharp
services.AddTransient<IValidator<CreateUserCommand>, CreateUserCommandValidator>();
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

## Validation Behavior Execution Order

`ValidationBehavior` runs as part of the pipeline. Register it first to validate before other behaviors:

```csharp
// ValidationBehavior runs first (validates before logging)
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

Execution order:
```
Request → ValidationBehavior → LoggingBehavior → Handler
```

If validation fails, the pipeline short-circuits and subsequent behaviors/handlers are not executed.

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

For specific requests that need validation:

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

1. **Use DataAnnotations for simple validation** - Required, ranges, string lengths, formats
2. **Use custom validators for business logic** - Async database checks, complex rules
3. **Register ValidationBehavior first** - Fail fast before expensive operations
4. **Create specific error messages** - Help users understand what went wrong
5. **Include AttemptedValue in errors** - Useful for logging and debugging
6. **Handle ValidationException at API boundary** - Convert to HTTP 400 responses
7. **Register validators per-request** - Don't validate requests that don't need it

## See Also

- [Pipeline Behaviors](pipeline-behaviors.md) - Learn about behavior execution order
- [Quick Start](quick-start.md) - Basic MediatorLite setup
