using MediatorLite.Configuration;
using MediatorLite.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace MediatorLite;

/// <summary>
/// Extension methods for registering MediatorLite services with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MediatorLite runtime services (mediator instance and options) to the service collection.
    /// Use this together with the source-generated AddGeneratedHandlers() method.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configure">An optional action to configure <see cref="MediatorOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// services
    ///     .AddGeneratedHandlers()     // Source-generated: registers handlers, notifications, behaviors
    ///     .AddMediatorLite(options =>
    ///     {
    ///         options.EnableBuiltInLogging = false;
    ///         options.NotificationExecutionStrategy = NotificationExecutionStrategy.Parallel;
    ///     });
    /// </code>
    /// </example>
    public static IServiceCollection AddMediatorLite(
        this IServiceCollection services,
        Action<MediatorOptions>? configure = null)
    {
        var options = new MediatorOptions();
        configure?.Invoke(options);

        // Register options as singleton
        services.AddSingleton(options);

        // Register mediator
        services.Add(new ServiceDescriptor(
            typeof(IMediator),
            typeof(Mediator),
            options.MediatorLifetime));

        // Register behaviors added via options (open generics registered as IPipelineBehavior<,>)
        foreach (var behaviorType in options.BehaviorTypes)
        {
            if (behaviorType.IsGenericTypeDefinition)
            {
                services.Add(new ServiceDescriptor(
                    typeof(IPipelineBehavior<,>),
                    behaviorType,
                    options.HandlerLifetime));
            }
            else
            {
                // For closed types, find and register the specific interface
                var behaviorInterface = behaviorType.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

                if (behaviorInterface != null)
                {
                    services.Add(new ServiceDescriptor(
                        behaviorInterface,
                        behaviorType,
                        options.HandlerLifetime));
                }
            }
        }

        return services;
    }

    /// <summary>
    /// Adds a pipeline behavior to the service collection.
    /// </summary>
    /// <typeparam name="TBehavior">The behavior type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the behavior to.</param>
    /// <param name="lifetime">The service lifetime for the behavior.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddMediatorBehavior<TBehavior>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TBehavior : class
    {
        var behaviorType = typeof(TBehavior);

        if (behaviorType.IsGenericTypeDefinition)
        {
            services.Add(new ServiceDescriptor(
                typeof(IPipelineBehavior<,>),
                behaviorType,
                lifetime));
        }
        else
        {
            var behaviorInterface = behaviorType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

            if (behaviorInterface != null)
            {
                services.Add(new ServiceDescriptor(behaviorInterface, behaviorType, lifetime));
            }
            else
            {
                services.Add(new ServiceDescriptor(behaviorType, behaviorType, lifetime));
            }
        }

        return services;
    }
}
