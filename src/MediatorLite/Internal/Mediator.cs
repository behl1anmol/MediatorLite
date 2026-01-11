using MediatorLite.Configuration;
using MediatorLite.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MediatorLite.Internal;

/// <summary>
/// Internal implementation of the mediator.
/// </summary>
internal sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Mediator> _logger;
    private readonly MediatorOptions _options;

    public Mediator(
        IServiceProvider serviceProvider,
        ILogger<Mediator> logger,
        MediatorOptions options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var requestTypeName = requestType.Name;

        // Start OpenTelemetry activity if tracing is enabled
        using var activity = _options.EnableTracing
            ? MediatorActivitySource.Source.StartActivity(
                $"{MediatorActivitySource.ActivityNames.SendRequest} {requestTypeName}",
                ActivityKind.Internal)
            : null;

        activity?.SetTag(MediatorActivitySource.Tags.RequestType, requestType.FullName);
        activity?.SetTag(MediatorActivitySource.Tags.ResponseType, typeof(TResponse).FullName);

        // Log if enabled
        if (_options.EnableBuiltInLogging)
        {
            _logger.Log(_options.DefaultLogLevel, "Sending request {RequestType}", requestTypeName);
        }

        try
        {
            // Get the handler type
            var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));

            // Resolve the handler
            var handler = _serviceProvider.GetService(handlerType)
                ?? throw new InvalidOperationException(
                    $"No handler registered for request type {requestType.FullName}. " +
                    $"Ensure a handler implementing IRequestHandler<{requestTypeName}, {typeof(TResponse).Name}> is registered.");

            // Get pipeline behaviors
            var behaviors = GetPipelineBehaviors<TResponse>(requestType);

            // Build the pipeline
            var response = await ExecutePipeline(request, handler, behaviors, cancellationToken);

            if (_options.EnableBuiltInLogging)
            {
                _logger.Log(_options.DefaultLogLevel, "Request {RequestType} handled successfully", requestTypeName);
            }

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetTag(MediatorActivitySource.Tags.Error, true);
            activity?.SetTag(MediatorActivitySource.Tags.ErrorMessage, ex.Message);

            if (_options.EnableBuiltInLogging)
            {
                _logger.LogError(ex, "Error handling request {RequestType}", requestTypeName);
            }

            throw;
        }
    }

    public async ValueTask PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        var notificationType = typeof(TNotification);
        var notificationTypeName = notificationType.Name;

        // Start OpenTelemetry activity if tracing is enabled
        using var activity = _options.EnableTracing
            ? MediatorActivitySource.Source.StartActivity(
                $"{MediatorActivitySource.ActivityNames.PublishNotification} {notificationTypeName}",
                ActivityKind.Internal)
            : null;

        activity?.SetTag(MediatorActivitySource.Tags.NotificationType, notificationType.FullName);

        if (_options.EnableBuiltInLogging)
        {
            _logger.Log(_options.DefaultLogLevel, "Publishing notification {NotificationType}", notificationTypeName);
        }

        // Get handlers
        var handlers = _serviceProvider
            .GetServices<INotificationHandler<TNotification>>()
            .ToList();

        activity?.SetTag(MediatorActivitySource.Tags.HandlerCount, handlers.Count);

        if (handlers.Count == 0)
        {
            if (_options.EnableBuiltInLogging)
            {
                _logger.Log(_options.DefaultLogLevel, "No handlers registered for notification {NotificationType}", notificationTypeName);
            }
            return;
        }

        // Order handlers by attribute if present
        var orderedHandlers = OrderHandlers(handlers);

        // Get execution options (check for per-notification override)
        var (executionStrategy, errorStrategy) = GetNotificationOptions(notificationType);

        activity?.SetTag(MediatorActivitySource.Tags.ExecutionStrategy, executionStrategy.ToString());

        try
        {
            await ExecuteNotificationHandlers(notification, orderedHandlers, executionStrategy, errorStrategy, cancellationToken);

            if (_options.EnableBuiltInLogging)
            {
                _logger.Log(_options.DefaultLogLevel, "Notification {NotificationType} published to {HandlerCount} handlers",
                    notificationTypeName, handlers.Count);
            }
        }
        catch (Exception ex)
        {
            activity?.SetTag(MediatorActivitySource.Tags.Error, true);
            activity?.SetTag(MediatorActivitySource.Tags.ErrorMessage, ex.Message);

            if (_options.EnableBuiltInLogging)
            {
                _logger.LogError(ex, "Error publishing notification {NotificationType}", notificationTypeName);
            }

            throw;
        }
    }

    private List<object> GetPipelineBehaviors<TResponse>(Type requestType)
    {
        var behaviors = new List<object>();
        var responseType = typeof(TResponse);
        
        // Resolve behaviors registered directly with the interface type
        var behaviorInterfaceType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, responseType);
        var interfaceBehaviors = _serviceProvider.GetServices(behaviorInterfaceType);
        foreach (var behavior in interfaceBehaviors)
        {
            if (behavior != null)
            {
                behaviors.Add(behavior);
            }
        }

        // Also resolve behaviors registered via options
        foreach (var behaviorType in _options.BehaviorTypes)
        {
            Type closedBehaviorType;

            if (behaviorType.IsGenericTypeDefinition)
            {
                try
                {
                    closedBehaviorType = behaviorType.MakeGenericType(requestType, responseType);
                }
                catch (ArgumentException)
                {
                    // Generic constraints not satisfied, skip this behavior
                    continue;
                }
            }
            else
            {
                closedBehaviorType = behaviorType;
            }

            // Check if this behavior type was already resolved via interface
            if (behaviors.Any(b => b.GetType() == closedBehaviorType))
            {
                continue;
            }

            var behavior = _serviceProvider.GetService(closedBehaviorType);
            if (behavior != null)
            {
                behaviors.Add(behavior);
            }
        }

        return behaviors;
    }

    private async ValueTask<TResponse> ExecutePipeline<TResponse>(
        IRequest<TResponse> request,
        object handler,
        List<object> behaviors,
        CancellationToken cancellationToken)
    {
        var requestType = request.GetType();
        
        // Get the handler interface type for proper method resolution
        var handlerInterfaceType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));

        // Build the handler delegate
        RequestHandlerDelegate<TResponse> handlerDelegate = () =>
        {
            // Find the HandleAsync method on the interface, not the concrete type
            // This ensures we get the ValueTask<TResponse> version, not the shadowed ValueTask version
            var interfaceMap = handler.GetType().GetInterfaceMap(handlerInterfaceType);
            var handleAsyncIndex = Array.FindIndex(interfaceMap.InterfaceMethods, 
                m => m.Name == "HandleAsync" && m.ReturnType == typeof(ValueTask<TResponse>));
            
            var method = handleAsyncIndex >= 0 
                ? interfaceMap.TargetMethods[handleAsyncIndex] 
                : handlerInterfaceType.GetMethod("HandleAsync");
                
            return (ValueTask<TResponse>)method!.Invoke(handler, [request, cancellationToken])!;
        };

        // Wrap with behaviors (in reverse order so first registered runs first)
        for (int i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var currentDelegate = handlerDelegate;

            handlerDelegate = () =>
            {
                var behaviorInterfaceType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
                var interfaceMap = behavior.GetType().GetInterfaceMap(behaviorInterfaceType);
                var handleAsyncIndex = Array.FindIndex(interfaceMap.InterfaceMethods,
                    m => m.Name == "HandleAsync" && m.GetParameters().Length == 3);
                    
                var method = handleAsyncIndex >= 0
                    ? interfaceMap.TargetMethods[handleAsyncIndex]
                    : behaviorInterfaceType.GetMethod("HandleAsync");

                return (ValueTask<TResponse>)method!.Invoke(behavior, [request, currentDelegate, cancellationToken])!;
            };
        }

        try
        {
            return await handlerDelegate();
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            // Unwrap reflection-induced exception wrapping
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw; // Unreachable but required for compiler
        }
    }

    private static List<INotificationHandler<TNotification>> OrderHandlers<TNotification>(
        List<INotificationHandler<TNotification>> handlers)
        where TNotification : INotification
    {
        return [.. handlers.OrderBy(h =>
        {
            var orderAttr = h.GetType().GetCustomAttribute<NotificationHandlerOrderAttribute>();
            return orderAttr?.Order ?? 0;
        })];
    }

    private (NotificationExecutionStrategy, NotificationErrorStrategy) GetNotificationOptions(Type notificationType)
    {
        var attr = notificationType.GetCustomAttribute<NotificationOptionsAttribute>();

        if (attr != null && attr.OverrideGlobal)
        {
            return (attr.ExecutionStrategy, attr.ErrorStrategy);
        }

        return (_options.NotificationExecutionStrategy, _options.NotificationErrorStrategy);
    }

    private async ValueTask ExecuteNotificationHandlers<TNotification>(
        TNotification notification,
        List<INotificationHandler<TNotification>> handlers,
        NotificationExecutionStrategy executionStrategy,
        NotificationErrorStrategy errorStrategy,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        switch (executionStrategy)
        {
            case NotificationExecutionStrategy.Sequential:
                await ExecuteSequential(notification, handlers, errorStrategy, cancellationToken);
                break;

            case NotificationExecutionStrategy.Parallel:
                await ExecuteParallel(notification, handlers, errorStrategy, cancellationToken);
                break;

            case NotificationExecutionStrategy.StopOnFirst:
                await ExecuteStopOnFirst(notification, handlers, cancellationToken);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(executionStrategy));
        }
    }

    private async ValueTask ExecuteSequential<TNotification>(
        TNotification notification,
        List<INotificationHandler<TNotification>> handlers,
        NotificationErrorStrategy errorStrategy,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        var exceptions = new List<Exception>();

        foreach (var handler in handlers)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await handler.HandleAsync(notification, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (errorStrategy == NotificationErrorStrategy.StopOnFirstError)
                {
                    throw;
                }

                exceptions.Add(ex);
            }
        }

        if (exceptions.Count > 0)
        {
            throw new AggregateException(
                $"One or more notification handlers for {typeof(TNotification).Name} threw exceptions.",
                exceptions);
        }
    }

    private async ValueTask ExecuteParallel<TNotification>(
        TNotification notification,
        List<INotificationHandler<TNotification>> handlers,
        NotificationErrorStrategy errorStrategy,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        // Wrap each handler invocation to catch synchronous exceptions
        // that would otherwise escape the LINQ Select before Task.WhenAll
        var tasks = new List<Task>(handlers.Count);
        foreach (var handler in handlers)
        {
            try
            {
                tasks.Add(handler.HandleAsync(notification, cancellationToken).AsTask());
            }
            catch (Exception ex)
            {
                // Synchronous exception - wrap in a faulted task
                tasks.Add(Task.FromException(ex));
            }
        }

        if (errorStrategy == NotificationErrorStrategy.StopOnFirstError)
        {
            await Task.WhenAll(tasks);
        }
        else
        {
            var exceptions = new List<Exception>();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // Collect all exceptions from faulted tasks
                foreach (var task in tasks)
                {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        exceptions.AddRange(task.Exception.InnerExceptions);
                    }
                }
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(
                    $"One or more notification handlers for {typeof(TNotification).Name} threw exceptions.",
                    exceptions);
            }
        }
    }

    private static async ValueTask ExecuteStopOnFirst<TNotification>(
        TNotification notification,
        List<INotificationHandler<TNotification>> handlers,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler.HandleAsync(notification, cancellationToken);
            return; // Stop after first handler completes
        }
    }
}
