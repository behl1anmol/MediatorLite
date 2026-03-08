# Validation Reference

Detailed documentation for the MediatorLite validation subsystem in `src/MediatorLite/Validation/`.

---

## IValidator\<T\>

```csharp
namespace MediatorLite.Validation;

public interface IValidator<in TRequest>
{
    ValueTask<ValidationResult> ValidateAsync(TRequest request, CancellationToken cancellationToken = default);
}
```

- Contravariant in `TRequest` — a validator for a base type can validate derived types.
- Returns `ValueTask<ValidationResult>` — synchronous validators avoid allocation.
- Multiple validators can be registered per request type in DI; all are invoked by `ValidationBehavior`.

---

## DataAnnotationsValidator\<T\>

Located in `src/MediatorLite/Validation/DataAnnotationsValidator.cs`.

```csharp
namespace MediatorLite.Validation;

public class DataAnnotationsValidator<TRequest> : IValidator<TRequest>
{
    public ValueTask<ValidationResult> ValidateAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Null check → Failure("Request", "Request cannot be null")
        // 2. System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
        //        request, new ValidationContext(request), results, validateAllProperties: true)
        // 3. Maps System.ComponentModel.DataAnnotations.ValidationResult → ValidationError
        //    PropertyName = string.Join(", ", r.MemberNames)
        //    ErrorMessage = r.ErrorMessage ?? "Validation failed"
    }
}
```

How it works:
1. If `request` is `null`, immediately returns `ValidationResult.Failure(new ValidationError("Request", "Request cannot be null"))`.
2. Creates a `System.ComponentModel.DataAnnotations.ValidationContext` for the request.
3. Calls `Validator.TryValidateObject` with `validateAllProperties: true` (validates all properties, not just `[Required]`).
4. If valid → returns `ValidationResult.Success`.
5. If invalid → maps each `System.ComponentModel.DataAnnotations.ValidationResult` to a `ValidationError`:
   - `PropertyName` = joined `MemberNames` (comma-separated)
   - `ErrorMessage` = the error message or `"Validation failed"` as fallback
6. Returns `ValidationResult.Failure(errors)`.

**Note:** This class is `public` (not `sealed`), allowing consumers to extend it.

---

## ValidationBehavior\<TRequest, TResponse\>

Located in `src/MediatorLite/Validation/ValidationBehavior.cs`.

```csharp
namespace MediatorLite.Validation;

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private IReadOnlyList<IValidator<TRequest>> Validators { get; }

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        Validators = validators.ToList();
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        if (Validators.Count == 0)
            return await next();  // No validators → pass through

        var allErrors = new List<ValidationError>();

        foreach (var validator in Validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            if (!result.IsValid)
                allErrors.AddRange(result.Errors);
        }

        if (allErrors.Count > 0)
            throw new ValidationException(allErrors);

        return await next();
    }
}
```

Pipeline behavior flow:
1. Resolves all `IValidator<TRequest>` from DI via constructor injection.
2. If no validators registered → immediately calls `next()` (pass-through).
3. Runs each validator sequentially, collecting all errors.
4. If any errors → throws `ValidationException` with the aggregated error list (short-circuits — `next()` is never called).
5. If no errors → calls `next()` to continue the pipeline.

Key characteristics:
- **Sealed** — cannot be extended.
- **Aggregates all errors** — runs every validator before throwing, so all validation failures are reported at once.
- **Short-circuits pipeline** — when validation fails, the handler and subsequent behaviors never execute.

---

## ValidationResult

Located in `src/MediatorLite/Validation/Models/ValidationResult.cs`.

```csharp
namespace MediatorLite.Validation.Models;

public sealed class ValidationResult
{
    public static ValidationResult Success { get; } = new() { IsValid = true };

    public bool IsValid { get; private init; }
    public IReadOnlyList<ValidationError> Errors { get; private init; } = [];

    public static ValidationResult Failure(params ValidationError[] errors)
    {
        return new ValidationResult { IsValid = false, Errors = errors };
    }

    public static ValidationResult Failure(IEnumerable<ValidationError> errors)
    {
        return new ValidationResult { IsValid = false, Errors = errors.ToList() };
    }
}
```

Key design:
- **Sealed class** with `private init` setters — immutable after construction.
- **`Success` singleton** — static property, avoids allocations for the common success case. `IsValid = true`, `Errors = []`.
- **Two `Failure` factory methods:**
  - `params ValidationError[]` — convenient for small fixed lists.
  - `IEnumerable<ValidationError>` — for dynamic lists (materializes to `List<T>`).
- `Errors` defaults to `[]` (empty collection literal) — never null.

---

## ValidationError

Located in `src/MediatorLite/Validation/Models/ValidationError.cs`.

```csharp
namespace MediatorLite.Validation.Models;

public sealed record ValidationError(
    string PropertyName,
    string ErrorMessage,
    object? AttemptedValue = null);
```

- **Sealed record** — immutable, value equality.
- `PropertyName` — the name of the property that failed validation.
- `ErrorMessage` — descriptive error message.
- `AttemptedValue` — optional, the value that was attempted (defaults to `null`).

---

## ValidationException

Located in `src/MediatorLite/Validation/ValidationException.cs`.

```csharp
namespace MediatorLite.Validation;

public sealed class ValidationException : Exception
{
    public IReadOnlyList<ValidationError> Errors { get; }

    // Public constructors:
    public ValidationException(IEnumerable<ValidationError> errors)
        : this([.. errors]) { }

    public ValidationException(string message, IEnumerable<ValidationError> errors)
        : base(message)
    {
        Errors = errors.ToList();
    }

    // Private constructor (used by the public IEnumerable overload):
    private ValidationException(IReadOnlyList<ValidationError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }
}
```

**Auto-generated message via `BuildMessage`:**
- 0 errors: `"Validation failed."`
- 1 error: `"Validation failed: {errors[0].ErrorMessage}"`
- N errors: `"Validation failed with N errors: msg1, msg2, ..."` (uses `StringBuilder`, `AppendJoin`)

Key design:
- **Sealed** — cannot be subclassed.
- Exposes `IReadOnlyList<ValidationError> Errors` for programmatic error inspection.
- Two constructors: auto-message from errors, or custom message with errors.

---

## Registration Pattern

### Source-Generated Registration

When using the source generator, `ValidationBehavior<,>` is automatically registered as the **first behavior** in the pipeline, ensuring validation runs before any other behaviors:

```csharp
// Generated code (conceptual):
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

### Manual Registration

For manual DI setup:

```csharp
// Register validators
services.AddTransient<IValidator<CreateUserCommand>, CreateUserCommandValidator>();

// Register ValidationBehavior as an open generic pipeline behavior
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// Or via MediatorOptions:
services.AddMediatorLite(options =>
{
    options.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

// Or via AddMediatorBehavior:
services.AddMediatorBehavior<ValidationBehavior<CreateUserCommand, int>>();
```

### Custom Validators

Implement `IValidator<T>` for custom validation logic:

```csharp
public class CreateUserCommandValidator : IValidator<CreateUserCommand>
{
    public async ValueTask<ValidationResult> ValidateAsync(
        CreateUserCommand request, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add(new ValidationError(nameof(request.Name), "Name is required"));

        if (errors.Count > 0)
            return ValidationResult.Failure(errors);

        return ValidationResult.Success;
    }
}
```

Multiple validators per request type are supported — all are invoked, and errors are aggregated.
