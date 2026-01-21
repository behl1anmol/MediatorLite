using MediatorLite.Configuration;
using MediatorLite.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace MediatorLite.Internal;

/// <summary>
/// Internal implementation of the mediator with optimized dispatch using cached delegates.
/// </summary>
internal sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Mediator> _logger;
    private readonly MediatorOptions _options;

    // Static caches for type lookups - shared across all Mediator instances
    private static readonly ConcurrentDictionary<Type, Type> _handlerTypeCache = new();
    private static readonly ConcurrentDictionary<(Type, Type), Type> _behaviorTypeCache = new();
    
    // Cached compiled delegates for handler invocation (avoids reflection per request)
    private static readonly ConcurrentDictionary<Type, Delegate> _handlerDelegateCache = new();
    private static readonly ConcurrentDictionary<Type, Delegate> _behaviorDelegateCache = new();
    
    // Cached notification handler orderings
    private static readonly ConcurrentDictionary<Type, int> _handlerOrderCache = new();

    public Mediator(
        IServiceProvider serviceProvider,
        ILogger<Mediator> logger,
        MediatorOptions options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Clears all static caches. Useful for testing scenarios.
    /// </summary>
    internal static void ClearCaches()
    {
        _handlerTypeCache.Clear();
        _behaviorTypeCache.Clear();
        _handlerDelegateCache.Clear();
        _behaviorDelegateCache.Clear();
        _handlerOrderCache.Clear();
    }

    public async Task<TResponse> SendAsync<TResponse>(
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
            // Get cached handler type
            var handlerType = _handlerTypeCache.GetOrAdd(
                requestType,
                static rt => typeof(IRequestHandler<,>).MakeGenericType(rt, rt.GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
                    .GetGenericArguments()[0]));

            // For the specific response type, we need a more precise cache key
            var specificHandlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));

            // Resolve the handler
            var handler = _serviceProvider.GetService(specificHandlerType)
                ?? throw new InvalidOperationException(
                    $"No handler registered for request type {requestType.FullName}. " +
                    $"Ensure a handler implementing IRequestHandler<{requestTypeName}, {typeof(TResponse).Name}> is registered.");

            // Get pipeline behaviors
            var behaviors = GetPipelineBehaviors<TResponse>(requestType);

            // Fast path: no behaviors, invoke handler directly using cached delegate
            if (behaviors.Count == 0)
            {
                var response = await InvokeHandlerDirectly<TResponse>(handler, request, cancellationToken);
                
                if (_options.EnableBuiltInLogging)
                {
                    _logger.Log(_options.DefaultLogLevel, "Request {RequestType} handled successfully", requestTypeName);
                }
                
                return response;
            }

            // Build and execute the pipeline
            var result = await ExecutePipeline(request, handler, behaviors, cancellationToken);

            if (_options.EnableBuiltInLogging)
            {
                _logger.Log(_options.DefaultLogLevel, "Request {RequestType} handled successfully", requestTypeName);
            }

            return result;
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

    public async Task PublishAsync<TNotification>(
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

        // Order handlers by attribute if present (uses cached order values)
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

    /// <summary>
    /// Invokes the handler directly using a cached compiled delegate (fast path for no behaviors).
    /// </summary>
    private ValueTask<TResponse> InvokeHandlerDirectly<TResponse>(
        object handler,
        IRequest<TResponse> request,
        CancellationToken cancellationToken)
    {
        var requestType = request.GetType();
        var cacheKey = requestType;

        var invoker = _handlerDelegateCache.GetOrAdd(cacheKey, _ =>
        {
            return CreateHandlerDelegate<TResponse>(requestType);
        });

        return ((Func<object, object, CancellationToken, ValueTask<TResponse>>)invoker)(handler, request, cancellationToken);
    }

    /// <summary>
    /// Creates a compiled delegate for invoking handler.HandleAsync without reflection.
    /// </summary>
    private static Func<object, object, CancellationToken, ValueTask<TResponse>> CreateHandlerDelegate<TResponse>(Type requestType)
    {
        // Parameters: handler (object), request (object), cancellationToken
        var handlerParam = Expression.Parameter(typeof(object), "handler");
        var requestParam = Expression.Parameter(typeof(object), "request");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        var handlerInterfaceType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        
        // Cast handler to the specific interface
        var castHandler = Expression.Convert(handlerParam, handlerInterfaceType);
        
        // Cast request to the specific type
        var castRequest = Expression.Convert(requestParam, requestType);

        // Get the HandleAsync method
        var handleMethod = handlerInterfaceType.GetMethod("HandleAsync")!;

        // Call handler.HandleAsync(request, cancellationToken)
        var call = Expression.Call(castHandler, handleMethod, castRequest, ctParam);

        // Compile and return
        var lambda = Expression.Lambda<Func<object, object, CancellationToken, ValueTask<TResponse>>>(
            call, handlerParam, requestParam, ctParam);

        return lambda.Compile();
    }

    private List<object> GetPipelineBehaviors<TResponse>(Type requestType)
    {
        var behaviors = new List<object>();
        var responseType = typeof(TResponse);

        // Get cached behavior interface type
        var behaviorInterfaceType = _behaviorTypeCache.GetOrAdd(
            (requestType, responseType),
            static key => typeof(IPipelineBehavior<,>).MakeGenericType(key.Item1, key.Item2));

        // Resolve behaviors registered directly with the interface type
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
                // Use cache for MakeGenericType
                closedBehaviorType = _behaviorTypeCache.GetOrAdd(
                    (behaviorType, requestType),
                    key =>
                    {
                        try
                        {
                            return key.Item1.MakeGenericType(requestType, responseType);
                        }
                        catch (ArgumentException)
                        {
                            // Generic constraints not satisfied
                            return null!;
                        }
                    });

                if (closedBehaviorType == null)
                {
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

        // Build the handler delegate using cached compiled delegate
        RequestHandlerDelegate<TResponse> handlerDelegate = () =>
            InvokeHandlerDirectly<TResponse>(handler, request, cancellationToken);

        // Wrap with behaviors (in reverse order so first registered runs first)
        for (int i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var currentDelegate = handlerDelegate;

            handlerDelegate = () =>
                InvokeBehavior<TResponse>(behavior, request, currentDelegate, cancellationToken);
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

    /// <summary>
    /// Invokes a behavior using a cached compiled delegate.
    /// </summary>
    private ValueTask<TResponse> InvokeBehavior<TResponse>(
        object behavior,
        IRequest<TResponse> request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestType = request.GetType();
        var behaviorType = behavior.GetType();
        var cacheKey = behaviorType;

        var invoker = _behaviorDelegateCache.GetOrAdd(cacheKey, _ =>
        {
            return CreateBehaviorDelegate<TResponse>(requestType, behaviorType);
        });

        return ((Func<object, object, RequestHandlerDelegate<TResponse>, CancellationToken, ValueTask<TResponse>>)invoker)(
            behavior, request, next, cancellationToken);
    }

    /// <summary>
    /// Creates a compiled delegate for invoking behavior.HandleAsync without reflection.
    /// </summary>
    private static Func<object, object, RequestHandlerDelegate<TResponse>, CancellationToken, ValueTask<TResponse>> 
        CreateBehaviorDelegate<TResponse>(Type requestType, Type behaviorType)
    {
        // Parameters: behavior (object), request (object), next (delegate), cancellationToken
        var behaviorParam = Expression.Parameter(typeof(object), "behavior");
        var requestParam = Expression.Parameter(typeof(object), "request");
        var nextParam = Expression.Parameter(typeof(RequestHandlerDelegate<TResponse>), "next");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        var behaviorInterfaceType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));

        // Cast behavior to the specific interface
        var castBehavior = Expression.Convert(behaviorParam, behaviorInterfaceType);

        // Cast request to the specific type
        var castRequest = Expression.Convert(requestParam, requestType);

        // Get the HandleAsync method
        var handleMethod = behaviorInterfaceType.GetMethod("HandleAsync")!;

        // Call behavior.HandleAsync(request, next, cancellationToken)
        var call = Expression.Call(castBehavior, handleMethod, castRequest, nextParam, ctParam);

        // Compile and return
        var lambda = Expression.Lambda<Func<object, object, RequestHandlerDelegate<TResponse>, CancellationToken, ValueTask<TResponse>>>(
            call, behaviorParam, requestParam, nextParam, ctParam);

        return lambda.Compile();
    }

    private static List<INotificationHandler<TNotification>> OrderHandlers<TNotification>(
        List<INotificationHandler<TNotification>> handlers)
        where TNotification : INotification
    {
        return [.. handlers.OrderBy(h =>
        {
            var handlerType = h.GetType();
            
            // Use cached order value
            return _handlerOrderCache.GetOrAdd(handlerType, static type =>
            {
                var orderAttr = type.GetCustomAttribute<NotificationHandlerOrderAttribute>();
                return orderAttr?.Order ?? 0;
            });
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

    private static async ValueTask ExecuteSequential<TNotification>(
        TNotification notification,
        List<INotificationHandler<TNotification>> handlers,
        NotificationErrorStrategy errorStrategy,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        // Lazy exception list allocation - only allocate when needed
        List<Exception>? exceptions = null;

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

                (exceptions ??= []).Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException(
                $"One or more notification handlers for {typeof(TNotification).Name} threw exceptions.",
                exceptions);
        }
    }

    private static async ValueTask ExecuteParallel<TNotification>(
        TNotification notification,
        List<INotificationHandler<TNotification>> handlers,
        NotificationErrorStrategy errorStrategy,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        var count = handlers.Count;
        
        // Use ArrayPool to avoid allocating a new array for each notification
        var rentedArray = ArrayPool<Task>.Shared.Rent(count);
        try
        {
            // Fill the array with handler tasks
            // Note: ValueTask must be converted to Task for Task.WhenAll parallel execution.
            // This allocates a Task wrapper per handler, but is unavoidable for concurrent execution.
            for (int i = 0; i < count; i++)
            {
                try
                {
                    rentedArray[i] = handlers[i].HandleAsync(notification, cancellationToken).AsTask();
                }
                catch (Exception ex)
                {
                    // Synchronous exception - wrap in a faulted task
                    rentedArray[i] = Task.FromException(ex);
                }
            }

            // Create a span of just the tasks we need and use explicit cast to avoid ambiguity
            var tasksSpan = rentedArray.AsSpan(0, count);

            if (errorStrategy == NotificationErrorStrategy.StopOnFirstError)
            {
                await Task.WhenAll(tasksSpan.ToArray());
            }
            else
            {
                List<Exception>? exceptions = null;

                try
                {
                    await Task.WhenAll(tasksSpan.ToArray());
                }
                catch
                {
                    // Collect all exceptions from faulted tasks
                    for (int i = 0; i < count; i++)
                    {
                        var task = rentedArray[i];
                        if (task.IsFaulted && task.Exception != null)
                        {
                            (exceptions ??= []).AddRange(task.Exception.InnerExceptions);
                        }
                    }
                }

                if (exceptions is { Count: > 0 })
                {
                    throw new AggregateException(
                        $"One or more notification handlers for {typeof(TNotification).Name} threw exceptions.",
                        exceptions);
                }
            }
        }
        finally
        {
            // Return the rented array - clear it to avoid holding references
            Array.Clear(rentedArray, 0, count);
            ArrayPool<Task>.Shared.Return(rentedArray);
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
