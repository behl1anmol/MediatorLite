using MediatorLite.Validation.Models;

namespace MediatorLite.Validation;

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