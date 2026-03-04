using MediatorLite.Validation.Models;

namespace MediatorLite.Validation;

/// <summary>
/// A pipeline behavior that validates requests using registered validators.
/// </summary>
/// <typeparam name="TRequest">The type of request.</typeparam>
/// <typeparam name="TResponse">The type of response.</typeparam>
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
