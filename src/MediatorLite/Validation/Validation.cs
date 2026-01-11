using System.ComponentModel.DataAnnotations;

namespace MediatorLite.Validation;

/// <summary>
/// Result of a validation operation.
/// </summary>
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

/// <summary>
/// Represents a single validation error.
/// </summary>
/// <param name="PropertyName">The name of the property that failed validation.</param>
/// <param name="ErrorMessage">The error message describing the validation failure.</param>
/// <param name="AttemptedValue">The value that was attempted.</param>
public sealed record ValidationError(
    string PropertyName,
    string ErrorMessage,
    object? AttemptedValue = null);

/// <summary>
/// Defines a validator for a specific request type.
/// </summary>
/// <typeparam name="TRequest">The type of request to validate.</typeparam>
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

/// <summary>
/// Exception thrown when validation fails.
/// </summary>
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
        : base(BuildMessage(errors))
    {
        Errors = errors.ToList();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errors">The validation errors.</param>
    public ValidationException(string message, IEnumerable<ValidationError> errors)
        : base(message)
    {
        Errors = errors.ToList();
    }

    private static string BuildMessage(IEnumerable<ValidationError> errors)
    {
        var errorList = errors.ToList();
        if (errorList.Count == 0)
        {
            return "Validation failed.";
        }

        if (errorList.Count == 1)
        {
            return $"Validation failed: {errorList[0].ErrorMessage}";
        }

        return $"Validation failed with {errorList.Count} errors: {string.Join(", ", errorList.Select(e => e.ErrorMessage))}";
    }
}

/// <summary>
/// A pipeline behavior that validates requests using registered validators.
/// </summary>
/// <typeparam name="TRequest">The type of request.</typeparam>
/// <typeparam name="TResponse">The type of response.</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="validators">The validators for the request type.</param>
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var validators = _validators.ToList();
        if (validators.Count == 0)
        {
            return await next();
        }

        var allErrors = new List<ValidationError>();

        foreach (var validator in validators)
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

/// <summary>
/// A simple validator that uses DataAnnotations attributes.
/// </summary>
/// <typeparam name="TRequest">The type of request to validate.</typeparam>
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
