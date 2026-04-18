using MediatorLite.Configuration;
using MediatorLite.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    ///         options.EnableTracing = true;
    ///     });
    ///
    /// // Notification execution/error strategies are compile-time only. Use attributes:
    /// //   [NotificationExecution(NotificationExecutionStrategy.Parallel)]
    /// //   [NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
    /// // on notification types, or assembly-level defaults:
    /// //   [assembly: DefaultNotificationExecution(NotificationExecutionStrategy.Parallel)]
    /// //   [assembly: DefaultNotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
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

        return services;
    }
}