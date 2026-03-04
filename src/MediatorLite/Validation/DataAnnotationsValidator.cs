using System.ComponentModel.DataAnnotations;
using MediatorLite.Validation.Models;
using ValidationResult = MediatorLite.Validation.Models.ValidationResult;

namespace MediatorLite.Validation;

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