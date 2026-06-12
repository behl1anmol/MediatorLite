# Validation

MediatorLite uses **[FluentValidation](https://docs.fluentvalidation.net/)** as its
validation engine. Validators are discovered at compile time by the source generator and
run as the **outermost pipeline behavior**, so invalid requests short-circuit before any
other behavior or the handler executes — with **zero reflection-based assembly scanning**.

## Overview

Validation lives in the opt-in **`MediatorLite.FluentValidation`** package (core
`MediatorLite` takes no dependency on FluentValidation). It provides:

- **`FluentValidationBehavior<TRequest, TResponse>`** — the pipeline behavior that runs all
  FluentValidation validators for a request and throws on failure. The source generator
  wires it in automatically; you never register it by hand.
- **`ValidationException`** (`MediatorLite.Validation`) — thrown when validation fails.
  FluentValidation failures are mapped onto this single, uniform exception type.
- **`ValidationError`** (`MediatorLite.Validation.Models`) — one mapped failure
  (`PropertyName`, `ErrorMessage`, `AttemptedValue`).

You author validators with FluentValidation's `AbstractValidator<T>` exactly as you would
anywhere else.

## Setup

Reference the integration package (alongside `MediatorLite` and the source generator):

```xml
<PackageReference Include="MediatorLite" Version="..." />
<PackageReference Include="MediatorLite.FluentValidation" Version="..." />
```

> If the generator discovers FluentValidation validators for handled request types but the
> `MediatorLite.FluentValidation` package is **not** referenced, the build fails with
> **`MEDL1001`** telling you to add the package. Validation never silently stops running.

## Quick Start

### 1. Define a request

```csharp
public record CreateUserCommand : IRequest<int>
{
    public required string Name { get; init; }
    public required string Email { get; init; }
    public int Age { get; init; }
}
```

### 2. Write a FluentValidation validator

```csharp
using FluentValidation;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .Length(2, 100).WithMessage("Name must be between 2 and 100 characters");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format");

        RuleFor(x => x.Age)
            .InclusiveBetween(18, 120).WithMessage("Age must be between 18 and 120");
    }
}
```

### 3. Register services

The source generator discovers the validator and wires `FluentValidationBehavior` for you.
There is **no** `AddValidatorsFromAssembly(...)` scan.

```csharp
using MediatorLite.Generated;

services
    .AddGeneratedHandlers()   // registers handlers, validators, behaviors — O(1) dispatch
    .AddMediatorLite();

// The source generator:
//   1. Discovers every concrete FluentValidation.IValidator<T> (AbstractValidator<T> subclasses)
//   2. Registers each against FluentValidation.IValidator<T> for the handled request type
//   3. Emits FluentValidationBehavior<T, _> as the OUTERMOST pipeline behavior for that type
```

For granular control, the individual methods still exist:

```csharp
services
    .AddGeneratedRequestHandlers()
    .AddGeneratedNotificationHandlers()
    .AddGeneratedValidators()       // registers discovered FluentValidation validators
    .AddGeneratedBehaviors()        // registers behaviors (validation first, then [BehaviorOrder])
    .AddMediatorLite();
```

> **Note:** Only validators whose request type has a handler **in the same compilation** are
> registered and counted in `ValidatorCount`. A validator for an unhandled request type has no
> generated pipeline to run in, so it is ignored.

### 4. Handle `ValidationException`

```csharp
using MediatorLite.Validation;

try
{
    var userId = await mediator.SendAsync(new CreateUserCommand
    {
        Name = "",                 // invalid: too short
        Email = "invalid-email",   // invalid: bad format
        Age = 15                   // invalid: below minimum
    });
}
catch (ValidationException ex)
{
    foreach (var error in ex.Errors)
    {
        Console.WriteLine($"{error.PropertyName}: {error.ErrorMessage}");
    }
}
```

## Async and database-backed rules

FluentValidation's async rules work out of the box — `FluentValidationBehavior` always calls
`ValidateAsync`. Validators are resolved from the request's DI scope, so injecting scoped
dependencies (such as a `DbContext`) is fine:

```csharp
public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator(IUserRepository users)
    {
        RuleFor(x => x.Email)
            .MustAsync(async (email, ct) => !await users.EmailExistsAsync(email, ct))
            .WithMessage("Email is already registered");
    }
}
```

## Multiple validators

Register more than one `AbstractValidator<T>` for the same request type and all of them run;
their failures are aggregated into a single `ValidationException`.

## Execution order

`FluentValidationBehavior` is always the **outermost** behavior for a validated request type —
it wraps (runs before) every `[BehaviorOrder]` behavior and the handler. `[BehaviorOrder]` only
orders the non-validation behaviors among themselves.

```
Request → FluentValidationBehavior → [BehaviorOrder(0)] Logging → … → Handler
```

If validation fails, the pipeline short-circuits: no other behavior and no handler runs.

## Error mapping

Each FluentValidation `ValidationFailure` is mapped to a MediatorLite `ValidationError`:

| FluentValidation `ValidationFailure` | MediatorLite `ValidationError` |
|--------------------------------------|--------------------------------|
| `PropertyName`                       | `PropertyName`                 |
| `ErrorMessage`                       | `ErrorMessage`                 |
| `AttemptedValue`                     | `AttemptedValue`               |

This gives you one exception type (`MediatorLite.Validation.ValidationException`) to catch at
your API boundary, regardless of the validators involved:

```csharp
catch (ValidationException ex)
{
    return Results.ValidationProblem(
        ex.Errors
          .GroupBy(e => e.PropertyName)
          .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
}
```

## Source generator diagnostics

```csharp
using MediatorLite.Generated;

Console.WriteLine($"Validators discovered: {MediatorLiteRegistration.ValidatorCount}");
```

## Testing validators

FluentValidation ships a test helper:

```csharp
using FluentValidation.TestHelper;

[Fact]
public void Validator_ShouldFailForInvalidEmail()
{
    var validator = new CreateUserCommandValidator();

    var result = validator.TestValidate(new CreateUserCommand
    {
        Name = "John Doe",
        Email = "invalid-email",
        Age = 25
    });

    result.ShouldHaveValidationErrorFor(x => x.Email);
}
```

## Migrating from the in-house validator (pre-FluentValidation)

Earlier versions shipped an in-house `MediatorLite.Validation.IValidator<T>`, a
`DataAnnotationsValidator<T>`, and a `ValidationResult`. These have been **removed**. To
migrate:

- Replace `IValidator<T>` implementations with `AbstractValidator<T>` (`RuleFor(...)`).
- Replace DataAnnotations (`[Required]`, `[Range]`, …) with equivalent FluentValidation rules
  (`NotEmpty()`, `InclusiveBetween(...)`, `EmailAddress()`, …).
- Add the `MediatorLite.FluentValidation` package reference.
- `ValidationException` and `ValidationError` are unchanged — existing `catch` blocks keep working.

## See Also

- [Pipeline Behaviors](pipeline-behaviors.md) — behavior execution order
- [Quick Start](quick-start.md) — basic MediatorLite setup
- [FluentValidation docs](https://docs.fluentvalidation.net/)
