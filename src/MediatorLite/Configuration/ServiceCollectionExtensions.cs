using MediatorLite.Configuration;
using MediatorLite.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace MediatorLite;

/// <summary>
/// Extension methods for registering MediatorLite services with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MediatorLite services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configure">An optional action to configure <see cref="MediatorOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddMediatorLite(options =>
    /// {
    ///     options.RegisterHandlersFromAssembly(typeof(Program).Assembly);
    ///     options.AddOpenBehavior(typeof(LoggingBehavior&lt;,&gt;));
    /// });
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

        // Register handlers from assemblies
        foreach (var assembly in options.Assemblies)
        {
            RegisterHandlersFromAssembly(services, assembly, options.HandlerLifetime);
        }

        // Register behaviors
        foreach (var behaviorType in options.BehaviorTypes)
        {
            if (behaviorType.IsGenericTypeDefinition)
            {
                services.Add(new ServiceDescriptor(
                    behaviorType,
                    behaviorType,
                    options.HandlerLifetime));
            }
            else
            {
                // For closed types, register as their specific interface
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
    /// Registers all request handlers and notification handlers from the specified assembly.
    /// </summary>
    private static void RegisterHandlersFromAssembly(
        IServiceCollection services,
        Assembly assembly,
        ServiceLifetime lifetime)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => !t.GetCustomAttributes<MediatorGenerationAttribute>().Any(a => a.Skip))
            .ToList();

        foreach (var handlerType in handlerTypes)
        {
            // Register request handlers
            var requestHandlerInterfaces = handlerType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));

            foreach (var interfaceType in requestHandlerInterfaces)
            {
                services.TryAdd(new ServiceDescriptor(interfaceType, handlerType, lifetime));
            }

            // Register notification handlers
            var notificationHandlerInterfaces = handlerType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>));

            foreach (var interfaceType in notificationHandlerInterfaces)
            {
                // Use Add instead of TryAdd for notification handlers (multiple handlers allowed)
                services.Add(new ServiceDescriptor(interfaceType, handlerType, lifetime));
            }
        }
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
            services.Add(new ServiceDescriptor(behaviorType, behaviorType, lifetime));
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

    /// <summary>
    /// Adds MediatorLite services using source-generated handler registrations.
    /// Call this after calling the generated AddGeneratedHandlers method.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configure">An optional action to configure <see cref="MediatorOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// Use this method when you want to use source generators for handler discovery.
    /// The source generator will create an extension method that you call before this one.
    /// </remarks>
    /// <example>
    /// <code>
    /// services
    ///     .AddGeneratedHandlers()  // Source-generated
    ///     .AddMediatorLiteCore(options =>
    ///     {
    ///         options.AddOpenBehavior(typeof(LoggingBehavior&lt;,&gt;));
    ///     });
    /// </code>
    /// </example>
    public static IServiceCollection AddMediatorLiteCore(
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

        // Register behaviors (handlers are assumed to be registered by source generator)
        foreach (var behaviorType in options.BehaviorTypes)
        {
            if (behaviorType.IsGenericTypeDefinition)
            {
                services.Add(new ServiceDescriptor(
                    behaviorType,
                    behaviorType,
                    options.HandlerLifetime));
            }
        }

        return services;
    }
}
