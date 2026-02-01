using MediatorLite;
using Microsoft.Extensions.Logging;

namespace MediatorLite.Sample.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;
        _logger.LogDebug("   [Behavior] Handling {RequestName}", requestName);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        _logger.LogDebug("   [Behavior] Handled {RequestName} in {ElapsedMs}ms", requestName, stopwatch.ElapsedMilliseconds);

        return response;
    }
}
