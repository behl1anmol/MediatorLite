---
name: mediatorlite-validation
description: Dual-layer validation model for MediatorLite. Covers the `IValidator<TRequest>` contract, the built-in attribute-driven `DataAnnotationsValidator<TRequest>`, the `ValidationBehavior<TRequest, TResponse>` pipeline behavior (auto-wired *first* by the source generator for every validated request type), the `ValidationException` / `ValidationResult` / `ValidationError` error contract, and how to register custom business-rule validators. Use when adding validation to a request, diagnosing missing or duplicate errors, deciding between DataAnnotations and custom validators, or reviewing hand-registration smells.
triggers: validation, validator, IValidator, DataAnnotationsValidator, ValidationBehavior, ValidationException, ValidationResult, ValidationError, AttemptedValue, PropertyName, FluentValidation adapter, [Required], [Range], [EmailAddress], auto-wired validation, validation error contract, dual-layer validation
---

# MediatorLite Validation

## Purpose

MediatorLite ships a **dual-layer validation model** that is auto-wired into the pipeline by the source generator. This skill teaches you the contract, the registration semantics, the error shape, and the anti-patterns agents must never introduce.

Two layers:

1. **`DataAnnotationsValidator<TRequest>`** — attribute-driven, zero-code, registered automatically for every request type that carries `[Required]`, `[Range]`, `[EmailAddress]`, `[StringLength]`, or any `ValidationAttribute`.
2. **`IValidator<TRequest>`** — your custom validator for business rules (DB lookups, cross-property invariants, async checks). All implementations are discovered at compile time.

Both layers feed the same `ValidationBehavior<TRequest, TResponse>`, which aggregates errors from **every** registered validator and throws a single `ValidationException` when any validator fails.

## When to use

- Adding validation to a new `IRequest<TResponse>`.
- Auditing why a request is (or isn't) being validated in integration tests.
- Reviewing PRs that hand-register `ValidationBehavior<,>` or an `IValidator<>` — this is almost always a smell in v2.
- Adapting FluentValidation, FluentResults, or a third-party validator library.
- Designing how validation errors surface at the API boundary (HTTP 400 / `ProblemDetails`).

## Entry points

- Behavior: [ValidationBehavior.cs](src/MediatorLite/Validation/ValidationBehavior.cs).
- Built-in attribute validator: [DataAnnotationsValidator.cs](src/MediatorLite/Validation/DataAnnotationsValidator.cs).
- Interface: [IValidator.cs](src/MediatorLite.Abstractions/Validation/IValidator.cs).
- Error contract: [ValidationException.cs](src/MediatorLite.Abstractions/Validation/ValidationException.cs), [Models/ValidationResult.cs](src/MediatorLite.Abstractions/Validation/Models/ValidationResult.cs), [Models/ValidationError.cs](src/MediatorLite.Abstractions/Validation/Models/ValidationError.cs).
- Source-generator wiring: [HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs) — search for `ValidationBehavior` / `DetermineValidationTargets` / `DataAnnotationsValidator`.
- End-user reference: [docs/validation.md](docs/validation.md).
- Test fixtures: [tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs](tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs) — see `ValidatedCommandCustomValidator`.

## API

### `IValidator<TRequest>` — the contract for custom validators

```9:18:src/MediatorLite.Abstractions/Validation/IValidator.cs
public interface IValidator<in TRequest>
{
    /// <summary>
    /// Validates the specified request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The validation result.</returns>
    ValueTask<ValidationResult> ValidateAsync(TRequest request, CancellationToken cancellationToken = default);
}
```

- Returns `ValueTask<ValidationResult>` (not `Task`) — matches the handler/behavior convention.
- `TRequest` is contravariant (`in`), enabling `IValidator<BaseRequest>` to validate derived requests if you need that pattern.
- Never throw for a validation failure — return `ValidationResult.Failure(...)`. The behavior converts aggregated failures into a single `ValidationException`.

### `DataAnnotationsValidator<TRequest>` — built-in attribute validator

```11:35:src/MediatorLite/Validation/DataAnnotationsValidator.cs
public class DataAnnotationsValidator<TRequest> : IValidator<TRequest>
{
    /// <inheritdoc />
    public ValueTask<ValidationResult> ValidateAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return ValueTask.FromResult(ValidationResult.Failure(
                new ValidationError("Request", "Request cannot be null")));
        }

        var context = new ValidationContext(request);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        if (Validator.TryValidateObject(request, context, results, validateAllProperties: true))
        {
            return ValueTask.FromResult(ValidationResult.Success);
        }

        var errors = results.Select(r => new ValidationError(
            string.Join(", ", r.MemberNames),
            r.ErrorMessage ?? "Validation failed")).ToList();

        return ValueTask.FromResult(ValidationResult.Failure(errors));
    }
}
```

- Uses the BCL `System.ComponentModel.DataAnnotations.Validator.TryValidateObject` with `validateAllProperties: true` — every attribute on every property is evaluated.
- `PropertyName` is `string.Join(", ", MemberNames)` — when an attribute targets multiple members (e.g. `[Compare]`), this concatenates them.
- **`AttemptedValue` is not populated** by the DataAnnotations path; only custom validators provide it.

### `ValidationBehavior<TRequest, TResponse>` — the pipeline behavior

```10:54:src/MediatorLite/Validation/ValidationBehavior.cs
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private IReadOnlyList<IValidator<TRequest>> Validators { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="validators">The validators for the request type.</param>
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        Validators = validators.ToList();
    }


    /// <inheritdoc />
    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        if (Validators.Count == 0)
        {
            return await next();
        }

        var allErrors = new List<ValidationError>();

        foreach (var validator in Validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            if (!result.IsValid)
            {
                allErrors.AddRange(result.Errors);
            }
        }

        if (allErrors.Count > 0)
        {
            throw new ValidationException(allErrors);
        }

        return await next();
    }
}
```

Semantics:

- **All** validators for `TRequest` run — there is no short-circuit on first failure.
- Errors from every validator are **concatenated** into a single `ValidationException`. Duplicate errors are not de-duplicated.
- If there are no validators registered (which is the case for request types with neither DataAnnotations nor a custom validator), the behavior simply calls `next()` — one extra indirection, no throw.
- The behavior is `sealed` and has no `[BehaviorOrder]`. It runs first because the source generator emits it first in the pipeline, not because of an attribute.

### Source-generator wiring

The generator inserts `ValidationBehavior<TReq, TRes>` **before** every other behavior for any request type that has at least one validator (custom or auto-generated DataAnnotations). The relevant logic is in [HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs) near `DetermineValidationTargets` and the branch that registers `ValidationBehavior<T,U>` "FIRST":

```752:762:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
        // Register ValidationBehavior FIRST for request types with validators
        // Register by concrete type so unrolled pipeline can resolve each behavior individually
        if (requestTypesWithValidation.Count > 0)
```

Do not rely on `[BehaviorOrder(0)]` to achieve this — the generator does it structurally.

### Error contract

```9:12:src/MediatorLite.Abstractions/Validation/Models/ValidationError.cs
public sealed record ValidationError(
    string PropertyName,
    string ErrorMessage,
    object? AttemptedValue = null);
```

```6:49:src/MediatorLite.Abstractions/Validation/Models/ValidationResult.cs
public sealed class ValidationResult
{
    /// <summary>
    /// Gets a successful validation result.
    /// </summary>
    public static ValidationResult Success { get; } = new() { IsValid = true };

    /// <summary>
    /// Gets whether the validation passed.
    /// </summary>
    public bool IsValid { get; private init; }

    /// <summary>
    /// Gets the validation errors, if any.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; private init; } = [];

    /// <summary>
    /// Creates a failed validation result with the specified errors.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    /// <returns>A failed <see cref="ValidationResult"/>.</returns>
    public static ValidationResult Failure(params ValidationError[] errors)
    {
        return new ValidationResult
        {
            IsValid = false,
            Errors = errors
        };
    }

    /// <summary>
    /// Creates a failed validation result with the specified errors.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    /// <returns>A failed <see cref="ValidationResult"/>.</returns>
    public static ValidationResult Failure(IEnumerable<ValidationError> errors)
    {
        return new ValidationResult
        {
            IsValid = false,
            Errors = errors.ToList()
        };
    }
}
```

Invariants:

- `Success` is a cached singleton. Prefer returning it over instantiating new successful results.
- `Failure(...)` with zero errors is legal but nonsensical — the behavior will still throw; don't do it.

### `ValidationException`

```9:65:src/MediatorLite.Abstractions/Validation/ValidationException.cs
public sealed class ValidationException : Exception
{
    /// <summary>
    /// Gets the validation errors that caused this exception.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    public ValidationException(IEnumerable<ValidationError> errors)
        : this([.. errors])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="errors">A readonly list of validation errors.</param>
    private ValidationException(IReadOnlyList<ValidationError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errors">The validation errors.</param>
    public ValidationException(string message, IEnumerable<ValidationError> errors)
        : base(message)
    {
        Errors = errors.ToList()
```

- `Message` is auto-built (`"Validation failed with N errors: ..."`). Override via the `(message, errors)` constructor only when you need a deterministic test string.
- `Errors` is always populated and never null.
- The exception is `sealed` — do not subclass; use additional `IValidator<T>` implementations instead.

## Patterns

### Custom validator — canonical shape

Reference test fixture:

```450:467:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
public class ValidatedCommandCustomValidator : IValidator<ValidatedCommand>
{
    public static bool WasExecuted { get; set; }
    public static void Reset() => WasExecuted = false;

    public ValueTask<MediatorValidationResult> ValidateAsync(ValidatedCommand request, CancellationToken cancellationToken = default)
    {
        WasExecuted = true;

        if (request.Name.Contains("blocked"))
        {
            return ValueTask.FromResult(MediatorValidationResult.Failure(
                new ValidationError("Name", "Name cannot contain 'blocked'")));
        }

        return ValueTask.FromResult(MediatorValidationResult.Success);
    }
}
```

(The benchmark project uses its **own** `IAppValidator<T>` abstraction at [tests/MediatorLite.RestApiBenchmarks/Application/Common/CreateOrderCommandValidator.cs](tests/MediatorLite.RestApiBenchmarks/Application/Common/CreateOrderCommandValidator.cs) — it is *not* a MediatorLite `IValidator<T>`. Don't cite it as such.)

### Async / database-backed validators

Implementations can take dependencies via the constructor; the DI lifetime matches however the generator registers them (transient by default). Use `cancellationToken` for any I/O call — `ValidationBehavior` passes it through.

### Mixing DataAnnotations with custom validators

Both layers coexist on the same request. The generator registers the `DataAnnotationsValidator<T>` for types carrying attributes **in addition to** any custom validators. `ValidationBehavior` runs all of them, concatenates errors, and throws once.

### FluentValidation integration

Provide a thin adapter implementing `IValidator<TRequest>` over `FluentValidation.IValidator<TRequest>`. See the snippet in [docs/validation.md](docs/validation.md). This is the only supported integration path — do not replace the pipeline position of `ValidationBehavior`.

## Common tasks

1. **Add validation to a new command**
   - Simple: decorate properties with `[Required]` / `[Range]` etc. The generator does the rest.
   - Business rules: add a `XyzCommandValidator : IValidator<XyzCommand>` in the assembly that invokes `AddGeneratedHandlers()`.
   - Rebuild and verify `MediatorLiteRegistration.ValidatorCount` increased.

2. **Surface errors at an HTTP boundary**
   - Catch `ValidationException` in an exception-mapping middleware and project `ex.Errors` into `ProblemDetails` / `HTTP 400`.

3. **Skip validation for a specific validator**
   - Decorate the validator class with `[MediatorGeneration(Skip = true)]` — note that this attribute is marked obsolete; prefer moving such validators out of the generated assembly.

4. **Unit test a validator in isolation**
   - Instantiate it directly and assert `(await v.ValidateAsync(request)).Errors`. Don't bring up the full `IMediator` pipeline for a pure rule check.

## Pitfalls

- **Hand-registering `ValidationBehavior<,>` alongside source generation.** The generator already registered it; you'll get duplicate aggregated errors or a double DI registration. Delete the manual call.
- **Hand-registering `DataAnnotationsValidator<T>` alongside source generation.** Same issue — duplicate errors. The generator auto-registers one for every annotated request type.
- **Placing the validator in a different assembly than the one invoking `AddGeneratedHandlers()`.** The source generator only sees types in the current compilation. Move the validator, or register it manually (and accept the reflection-fallback caveats).
- **Throwing from `ValidateAsync`.** The behavior does not catch — the exception will surface unwrapped. Always return `ValidationResult.Failure`.
- **Relying on short-circuit between validators.** All validators run. If you want to skip expensive checks after a cheap one fails, inline the cheap check into the expensive validator — do not split them.
- **Relying on a specific error order.** Errors are appended in validator-iteration order, which in turn depends on DI resolution order of `IEnumerable<IValidator<TRequest>>`. Treat ordering as best-effort only.
- **Confusing MediatorLite `IValidator<T>` with FluentValidation's `IValidator<T>`.** Same name, different namespace. Always fully qualify when both are in scope.
- **Expecting `AttemptedValue` from DataAnnotations.** It's always `null` there. Populate it in custom validators when you want it available at the API boundary.
- **Forgetting `[BehaviorOrder]` will *not* move ValidationBehavior.** The generator places it first structurally; applying `[BehaviorOrder(100)]` won't push it later.

## Related

- [docs/validation.md](docs/validation.md) — end-user reference.
- [docs/pipeline-behaviors.md](docs/pipeline-behaviors.md) — ordering semantics for complementary behaviors.
- [.cursor/skills/mediatorlite-abstractions/SKILL.md](.cursor/skills/mediatorlite-abstractions/SKILL.md) — for the underlying `IValidator<T>` / `ValidationException` contracts.
- [tests/MediatorLite.Tests/SourceGeneration/ValidationTests.cs](tests/MediatorLite.Tests/SourceGeneration/ValidationTests.cs) — regression fixtures for auto-wiring.
