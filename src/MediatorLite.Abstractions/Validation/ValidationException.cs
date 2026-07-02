using System.Text;
using MediatorLite.Validation.Models;

namespace MediatorLite.Validation;

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
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <see langword="null"/>.</exception>
    public ValidationException(IEnumerable<ValidationError> errors)
        : this(Freeze(errors))
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
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <see langword="null"/>.</exception>
    public ValidationException(string message, IEnumerable<ValidationError> errors)
        : base(message)
    {
        Errors = Freeze(errors);
    }

    // Snapshot into an array so Errors is genuinely immutable regardless of which
    // constructor ran — a live List<T> behind IReadOnlyList can be cast and mutated.
    private static ValidationError[] Freeze(IEnumerable<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return [.. errors];
    }

    private static string BuildMessage(IReadOnlyList<ValidationError> errors)
    {
        if (errors.Count == 0)
        {
            return "Validation failed.";
        }

        if (errors.Count == 1)
        {
            return $"Validation failed: {errors[0].ErrorMessage}";
        }

        var sb = new StringBuilder();
        sb.Append("Validation failed with ");
        sb.Append(errors.Count);
        sb.Append(" errors: ");
        sb.AppendJoin(", ", errors.Select(e => e.ErrorMessage));
        return sb.ToString();
    }
}