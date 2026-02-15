using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MediatorLite.Sample.SourceGen.Behaviors;

/// <summary>
/// Pipeline behavior that logs request execution time.
/// This is an open generic behavior that applies to all requests.
/// </summary>
public sealed class PerformanceLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<PerformanceLoggingBehavior<TRequest, TResponse>> _logger;

    public PerformanceLoggingBehavior(ILogger<PerformanceLoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug("⏱️ Starting {RequestName}", requestName);

        try
        {
            var response = await next();
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > 500)
            {
                _logger.LogWarning("⚠️ {RequestName} took {ElapsedMs}ms (slow)", requestName, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogDebug("✅ {RequestName} completed in {ElapsedMs}ms", requestName, stopwatch.ElapsedMilliseconds);
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ {RequestName} failed after {ElapsedMs}ms", requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
