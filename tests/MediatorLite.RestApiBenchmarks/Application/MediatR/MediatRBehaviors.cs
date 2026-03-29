using MediatorLite.RestApiBenchmarks.Application.Common;
using MR = global::MediatR;

namespace MediatorLite.RestApiBenchmarks.Application.MediatR;

public sealed class MediatRValidationBehavior<TRequest, TResponse> : MR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IAppValidator<TRequest>> _validators;

    public MediatRValidationBehavior(IEnumerable<IAppValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        MR.RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
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

public sealed class MediatRMetricsBehavior<TRequest, TResponse> : MR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        MR.RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
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

public sealed class MediatRLoggingBehavior<TRequest, TResponse> : MR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        MR.RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
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
