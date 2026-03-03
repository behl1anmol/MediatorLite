using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediatorLite.Configuration;

/// <summary>
/// Configuration options for the mediator.
/// </summary>
public sealed class MediatorOptions
{
    private readonly List<Type> _behaviorTypes = [];

    /// <summary>
    /// Gets the registered pipeline behavior types in order.
    /// </summary>
    internal IReadOnlyList<Type> BehaviorTypes => _behaviorTypes;

    /// <summary>
    /// Gets or sets the default execution strategy for notifications.
    /// Default is <see cref="NotificationExecutionStrategy.Sequential"/>.
    /// </summary>
    public NotificationExecutionStrategy NotificationExecutionStrategy { get; set; } =
        NotificationExecutionStrategy.Sequential;

    /// <summary>
    /// Gets or sets the default error handling strategy for notifications.
    /// Default is <see cref="NotificationErrorStrategy.ContinueAndAggregate"/>.
    /// </summary>
    public NotificationErrorStrategy NotificationErrorStrategy { get; set; } =
        NotificationErrorStrategy.ContinueAndAggregate;

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

    /// <summary>
    /// Adds an open generic pipeline behavior type.
    /// The behavior will be registered with the DI container as IPipelineBehavior&lt;,&gt;.
    /// </summary>
    /// <param name="behaviorType">The open generic behavior type (e.g., typeof(LoggingBehavior&lt;,&gt;)).</param>
    /// <returns>The <see cref="MediatorOptions"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when the type is not an open generic type.</exception>
    public MediatorOptions AddOpenBehavior(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (!behaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"Type {behaviorType.Name} must be an open generic type definition.",
                nameof(behaviorType));
        }

        var hasCorrectArity = behaviorType.GetGenericArguments().Length == 2;
        if (!hasCorrectArity)
        {
            throw new ArgumentException(
                $"Type {behaviorType.Name} must have exactly 2 generic type parameters.",
                nameof(behaviorType));
        }
        
        var implementsPipelineBehavior = behaviorType.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));
        if (!implementsPipelineBehavior)
        {
            throw new ArgumentException(
                $"Type '{behaviorType.Name}' must implement IPipelineBehavior<TRequest, TResponse>.",
                nameof(behaviorType));
        }
        
        _behaviorTypes.Add(behaviorType);
        return this;
    }

    /// <summary>
    /// Adds a closed pipeline behavior type.
    /// </summary>
    /// <typeparam name="TBehavior">The behavior type.</typeparam>
    /// <returns>The <see cref="MediatorOptions"/> instance for chaining.</returns>
    public MediatorOptions AddBehavior<TBehavior>() where TBehavior : class
    {
        _behaviorTypes.Add(typeof(TBehavior));
        return this;
    }
}
