using MediatorLite.Validation;
using MediatorLite.Validation.Models;

namespace MediatorLite.FluentValidation;

/// <summary>
/// Pipeline behavior that runs all registered FluentValidation validators for
/// <typeparamref name="TRequest"/> before the request reaches the handler.
/// </summary>
/// <remarks>
/// <para>
/// The MediatorLite source generator discovers concrete
/// <see cref="global::FluentValidation.IValidator{T}"/> implementations (typically
/// <see cref="global::FluentValidation.AbstractValidator{T}"/> subclasses) at compile
/// time and emits this behavior as the <em>outermost</em> pipeline arm for every
/// validated request type, so validation short-circuits before any other behavior or
/// the handler runs. There is no runtime assembly scanning.
/// </para>
/// <para>
/// All validators are run and their failures aggregated into a single
/// <see cref="ValidationException"/>. FluentValidation
/// <see cref="global::FluentValidation.Results.ValidationFailure"/> instances are
/// mapped to MediatorLite <see cref="ValidationError"/> so consumers catch one
/// uniform exception type regardless of the validation provider.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request type being validated.</typeparam>
/// <typeparam name="TResponse">The response type produced by the handler.</typeparam>
public sealed class FluentValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly global::FluentValidation.IValidator<TRequest>[] _validators;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="FluentValidationBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="validators">
    /// The FluentValidation validators for <typeparamref name="TRequest"/>, supplied by DI.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="validators"/> is null.</exception>
    public FluentValidationBehavior(IEnumerable<global::FluentValidation.IValidator<TRequest>> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);

        // Avoid an extra allocation when DI already handed us an array.
        _validators = validators as global::FluentValidation.IValidator<TRequest>[] ?? validators.ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        if (_validators.Length == 0)
        {
            return await next().ConfigureAwait(false);
        }

        // Lazily allocated: the happy path (all valid) allocates nothing here.
        List<ValidationError>? errors = null;

        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.IsValid)
            {
                continue;
            }

            var failures = result.Errors;
            errors ??= new List<ValidationError>(failures.Count);
            for (var i = 0; i < failures.Count; i++)
            {
                var failure = failures[i];
                // FluentValidation's low-level failure API permits null PropertyName /
                // ErrorMessage; ValidationError annotates both non-null, so coalesce here.
                errors.Add(new ValidationError(
                    failure.PropertyName ?? string.Empty,
                    failure.ErrorMessage ?? string.Empty,
                    failure.AttemptedValue));
            }
        }

        if (errors is not null)
        {
            throw new ValidationException(errors);
        }

        return await next().ConfigureAwait(false);
    }
}
