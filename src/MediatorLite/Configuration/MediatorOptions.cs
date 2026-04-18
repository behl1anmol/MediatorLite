using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediatorLite.Configuration;

/// <summary>
/// Configuration options for the mediator.
/// </summary>
public sealed class MediatorOptions
{
    /// <summary>
    /// Gets or sets whether built-in logging is enabled.
    /// Default is true.
    /// </summary>
    public bool EnableBuiltInLogging { get; set; } = true;

    /// <summary>
    /// Gets or sets the default log level for mediator operations.
    /// Default is <see cref="LogLevel.Debug"/>.
    /// </summary>
    public LogLevel DefaultLogLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// Gets or sets whether to enable OpenTelemetry tracing.
    /// Default is true.
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Gets or sets the service lifetime for handlers.
    /// Default is <see cref="ServiceLifetime.Transient"/>.
    /// </summary>
    public ServiceLifetime HandlerLifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// Gets or sets the service lifetime for the mediator.
    /// Default is <see cref="ServiceLifetime.Transient"/>.
    /// </summary>
    public ServiceLifetime MediatorLifetime { get; set; } = ServiceLifetime.Transient;
}
