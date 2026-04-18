using MediatorLite.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace MediatorLite;

/// <summary>
/// Extension methods for registering MediatorLite services with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MediatorLite runtime services (the mediator instance) to the service collection.
    /// Use this together with the source-generated <c>AddGeneratedHandlers()</c> method.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Built-in logging and tracing are on by default. To opt out at compile time, apply
    /// <c>[assembly: DisableMediatorLogging]</c> or <c>[assembly: DisableMediatorTracing]</c>
    /// in the consuming assembly.
    /// </para>
    /// <para>
    /// Log level is controlled via <c>Microsoft.Extensions.Logging</c> filter configuration.
    /// Notification execution and error strategies are compile-time only — use
    /// <c>[NotificationExecution]</c>/<c>[NotificationError]</c> per type or
    /// <c>[assembly: DefaultNotificationExecution]</c>/<c>[assembly: DefaultNotificationError]</c>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services
    ///     .AddGeneratedHandlers()
    ///     .AddMediatorLite();
    /// </code>
    /// </example>
    public static IServiceCollection AddMediatorLite(this IServiceCollection services)
    {
        services.AddTransient<IMediator, Mediator>();
        return services;
    }
}
