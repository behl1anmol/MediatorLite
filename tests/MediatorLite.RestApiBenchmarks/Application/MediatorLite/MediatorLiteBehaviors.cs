using MediatorLite.RestApiBenchmarks.Application.Common;
using ML = global::MediatorLite;

namespace MediatorLite.RestApiBenchmarks.Application.MediatorLite;

public sealed class MediatorLiteValidationBehavior<TRequest, TResponse> : ML.IPipelineBehavior<TRequest, TResponse>
    where TRequest : ML.IRequest<TResponse>
{
    private readonly IEnumerable<IAppValidator<TRequest>> _validators;

    public MediatorLiteValidationBehavior(IEnumerable<IAppValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        ML.RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var errors = new List<string>();
        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            errors.AddRange(result);
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }

        return await next();
    }
}

public sealed class MediatorLiteMetricsBehavior<TRequest, TResponse> : ML.IPipelineBehavior<TRequest, TResponse>
    where TRequest : ML.IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        ML.RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var response = await next();

        var signal = typeof(TRequest).Name.Length + typeof(TResponse).Name.Length;
        if (signal < 0)
        {
            throw new InvalidOperationException("Unreachable branch for JIT stabilization.");
        }

        return response;
    }
}

public sealed class MediatorLiteLoggingBehavior<TRequest, TResponse> : ML.IPipelineBehavior<TRequest, TResponse>
    where TRequest : ML.IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        ML.RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var start = DateTime.UtcNow.Ticks;
        var response = await next();
        var elapsedTicks = DateTime.UtcNow.Ticks - start;

        if (elapsedTicks < 0)
        {
            throw new InvalidOperationException("Clock skew detected.");
        }

        return response;
    }
}
