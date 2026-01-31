using MediatorLite.Configuration;
using MediatorLite.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Diagnostics;
using System.Reflection;

namespace MediatorLite.Internal;

/// <summary>
/// Internal implementation of the mediator with optimized dispatch.
/// Uses source-generated dispatch when available, falling back to direct reflection for dynamic scenarios.
/// </summary>
internal sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Mediator> _logger;
    private readonly MediatorOptions _options;
    private readonly ISourceGeneratedMediator? _sourceGeneratedMediator;

    public Mediator(
        IServiceProvider serviceProvider,
        ILogger<Mediator> logger,
        MediatorOptions options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // Try to get the source-generated mediator if available
        _sourceGeneratedMediator = serviceProvider.GetService<ISourceGeneratedMediator>();
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
            // Get pipeline behaviors
            var behaviors = GetPipelineBehaviors<TResponse>(requestType);

            // Fast path: no behaviors - try source-generated dispatch first
            if (behaviors.Count == 0)
            {
                var sourceGenResult = _sourceGeneratedMediator?.TrySendAsync<TResponse>(_serviceProvider, request, cancellationToken);
                if (sourceGenResult.HasValue)
                {
                    var response = await sourceGenResult.Value;

                    if (_options.EnableBuiltInLogging)
                    {
                        _logger.Log(_options.DefaultLogLevel, "Request {RequestType} handled successfully (source-generated)", requestTypeName);
                    }

                    return response;
                }
            }

            // Fallback to reflection-based dispatch (dynamic scenarios or when behaviors are present)
            var handlerInterfaceType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));

            var handler = _serviceProvider.GetService(handlerInterfaceType)
                ?? throw new InvalidOperationException(
                    $"No handler registered for request type {requestType.FullName}. " +
                    $"Ensure a handler implementing IRequestHandler<{requestTypeName}, {typeof(TResponse).Name}> is registered.");

            // No behaviors - invoke handler directly
            if (behaviors.Count == 0)
            {
                var response = await InvokeHandlerAsync<TResponse>(handler, handlerInterfaceType, request, cancellationToken);

                if (_options.EnableBuiltInLogging)
                {
                    _logger.Log(_options.DefaultLogLevel, "Request {RequestType} handled successfully", requestTypeName);
                }

                return response;
            }

            // Build and execute the pipeline with behaviors
            var result = await ExecutePipeline(request, handler, handlerInterfaceType, behaviors, cancellationToken);

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

    /// <summary>
    /// Invokes the handler using direct reflection.
    /// </summary>
    private static async ValueTask<TResponse> InvokeHandlerAsync<TResponse>(
        object handler,
        Type handlerInterfaceType,
        object request,
        CancellationToken cancellationToken)
    {
        var method = handlerInterfaceType.GetMethod("HandleAsync")!;

        try
        {
            var result = method.Invoke(handler, [request, cancellationToken]);
            return await (ValueTask<TResponse>)result!;
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw; // Unreachable
        }
    }

    /// <summary>
    /// Invokes a behavior using direct reflection.
    /// </summary>
    private static async ValueTask<TResponse> InvokeBehaviorAsync<TResponse>(
        object behavior,
        Type behaviorInterfaceType,
        object request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var method = behaviorInterfaceType.GetMethod("HandleAsync")!;

        try
        {
            var result = method.Invoke(behavior, [request, next, cancellationToken]);
            return await (ValueTask<TResponse>)result!;
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw; // Unreachable
        }
    }

    private List<object> GetPipelineBehaviors<TResponse>(Type requestType)
    {
        var behaviors = new List<object>();
        var responseType = typeof(TResponse);

        var behaviorInterfaceType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, responseType);

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
                try
                {
                    closedBehaviorType = behaviorType.MakeGenericType(requestType, responseType);
                }
                catch (ArgumentException)
                {
                    // Generic constraints not satisfied
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
        Type handlerInterfaceType,
        List<object> behaviors,
        CancellationToken cancellationToken)
    {
        var requestType = request.GetType();
        var behaviorInterfaceType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));

        // Build the innermost handler delegate
        RequestHandlerDelegate<TResponse> handlerDelegate = () =>
            InvokeHandlerAsync<TResponse>(handler, handlerInterfaceType, request, cancellationToken);

        // Wrap with behaviors (in reverse order so first registered runs first)
        for (int i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var currentDelegate = handlerDelegate;

            handlerDelegate = () =>
                InvokeBehaviorAsync<TResponse>(behavior, behaviorInterfaceType, request, currentDelegate, cancellationToken);
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

    private List<INotificationHandler<TNotification>> OrderHandlers<TNotification>(
        List<INotificationHandler<TNotification>> handlers)
        where TNotification : INotification
    {
        return [.. handlers.OrderBy(h =>
        {
            var handlerType = h.GetType();

            // Try source-generated order first
            var sourceGenOrder = _sourceGeneratedMediator?.TryGetHandlerOrder(handlerType);
            if (sourceGenOrder.HasValue)
            {
                return sourceGenOrder.Value;
            }

            // Fallback to direct reflection lookup
            var orderAttr = handlerType.GetCustomAttribute<NotificationHandlerOrderAttribute>();
            return orderAttr?.Order ?? 0;
        })];
    }

    private (NotificationExecutionStrategy, NotificationErrorStrategy) GetNotificationOptions(Type notificationType)
    {
        // Try source-generated options first
        var sourceGenOptions = _sourceGeneratedMediator?.TryGetNotificationOptions(notificationType);
        if (sourceGenOptions.HasValue)
        {
            return sourceGenOptions.Value;
        }

        // Fallback to direct reflection lookup
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

        var rentedArray = ArrayPool<Task>.Shared.Rent(count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                try
                {
                    rentedArray[i] = handlers[i].HandleAsync(notification, cancellationToken).AsTask();
                }
                catch (Exception ex)
                {
                    rentedArray[i] = Task.FromException(ex);
                }
            }

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
            return;
        }
    }
}
