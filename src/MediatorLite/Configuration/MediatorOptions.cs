using Microsoft.Extensions.Logging;
using System.Reflection;

namespace MediatorLite.Configuration;

/// <summary>
/// Configuration options for the mediator.
/// </summary>
public sealed class MediatorOptions
{
    private readonly List<Assembly> _assemblies = [];
    private readonly List<Type> _behaviorTypes = [];

    /// <summary>
    /// Gets the assemblies to scan for handlers.
    /// </summary>
    internal IReadOnlyList<Assembly> Assemblies => _assemblies;

    /// <summary>
    /// Gets the registered pipeline behavior types in order.
    /// </summary>
    internal IReadOnlyList<Type> BehaviorTypes => _behaviorTypes;

    /// <summary>
    /// Gets or sets the default execution strategy for notifications.
    /// Default is <see cref="NotificationExecutionStrategy.Sequential"/>.
    /// </summary>
    public NotificationExecutionStrategy NotificationExecutionStrategy { get; set; } = NotificationExecutionStrategy.Sequential;

    /// <summary>
    /// Gets or sets the default error handling strategy for notifications.
    /// Default is <see cref="NotificationErrorStrategy.ContinueAndAggregate"/>.
    /// </summary>
    public NotificationErrorStrategy NotificationErrorStrategy { get; set; } = NotificationErrorStrategy.ContinueAndAggregate;

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
    /// Default is <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient"/>.
    /// </summary>
    public Microsoft.Extensions.DependencyInjection.ServiceLifetime HandlerLifetime { get; set; } =
        Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient;

    /// <summary>
    /// Gets or sets the service lifetime for the mediator.
    /// Default is <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient"/>.
    /// </summary>
    public Microsoft.Extensions.DependencyInjection.ServiceLifetime MediatorLifetime { get; set; } =
        Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient;

    /// <summary>
    /// Registers handlers from the specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan for handlers.</param>
    /// <returns>The <see cref="MediatorOptions"/> instance for chaining.</returns>
    public MediatorOptions RegisterHandlersFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (!_assemblies.Contains(assembly))
        {
            _assemblies.Add(assembly);
        }
        return this;
    }

    /// <summary>
    /// Registers handlers from the assembly containing the specified type.
    /// </summary>
    /// <typeparam name="T">A type in the target assembly.</typeparam>
    /// <returns>The <see cref="MediatorOptions"/> instance for chaining.</returns>
    public MediatorOptions RegisterHandlersFromAssemblyContaining<T>()
    {
        return RegisterHandlersFromAssembly(typeof(T).Assembly);
    }

    /// <summary>
    /// Adds an open generic pipeline behavior type.
    /// </summary>
    /// <param name="behaviorType">The open generic behavior type (e.g., typeof(LoggingBehavior&lt;,&gt;)).</param>
    /// <returns>The <see cref="MediatorOptions"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when the type is not an open generic implementing IPipelineBehavior.</exception>
    public MediatorOptions AddOpenBehavior(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (!behaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"Type {behaviorType.Name} must be an open generic type definition.",
                nameof(behaviorType));
        }

        // Verify it implements IPipelineBehavior<,>
        var implementsInterface = behaviorType.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

        if (!implementsInterface)
        {
            // Check if any base type or the type itself when closed would implement the interface
            var hasConstrainedBehavior = behaviorType.GetGenericArguments().Length == 2;
            if (!hasConstrainedBehavior)
            {
                throw new ArgumentException(
                    $"Type {behaviorType.Name} must implement IPipelineBehavior<TRequest, TResponse>.",
                    nameof(behaviorType));
            }
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
