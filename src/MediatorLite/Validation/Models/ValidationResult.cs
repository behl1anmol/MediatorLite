namespace MediatorLite.Validation.Models;

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