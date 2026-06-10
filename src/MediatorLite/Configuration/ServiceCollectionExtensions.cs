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
    /// Adds a diagnostic fallback mediator to the service collection. The source-generated
    /// <c>AddGeneratedHandlers()</c> method registers the real <see cref="IMediator"/>
    /// implementation directly, so calling this method is optional; it exists to turn a
    /// missing source generator into a clear runtime error instead of a resolution failure.
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
        // TryAdd keeps this order-independent with AddGeneratedHandlers(): the generated
        // registration uses plain AddScoped, and the container resolves the last IMediator
        // descriptor, so the generated mediator wins regardless of call order.
        services.TryAddScoped<IMediator, ThrowingMediator>();
        return services;
    }
}
