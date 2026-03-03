using MediatorLite.Configuration;
using MediatorLite.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace MediatorLite.Internal;

/// <summary>
/// Internal implementation of the mediator with source-generated dispatch.
/// Uses source-generated dispatch for compile-time discovered types (zero reflection).
/// Falls back to minimal reflection only for dynamically registered types not known at compile time.
/// </summary>
internal sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Mediator> _logger;
    private readonly MediatorOptions _options;
    private readonly ISourceGeneratedMediator? _sourceGeneratedMediator;

    // Reflection fallback caches (used only when source-gen is not available)
    private static readonly ConcurrentDictionary<(Type, Type), Type> _handlerTypeCache = new();
    private static readonly ConcurrentDictionary<(Type, Type), Type> _behaviorTypeCache = new();
    private static readonly ConcurrentDictionary<Type, int> _handlerOrderCache = new();
    private static readonly ConcurrentDictionary<Type, (NotificationExecutionStrategy, NotificationErrorStrategy)?> _notificationOptionsCache = new();

    public Mediator(
        IServiceProvider serviceProvider,
        ILogger<Mediator> logger,
        MediatorOptions options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _sourceGeneratedMediator = serviceProvider.GetService<ISourceGeneratedMediator>();
    }

    public async Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var requestTypeName = requestType.Name;

        using var activity = _options.EnableTracing
            ? MediatorActivitySource.Source.StartActivity(
                $"{MediatorActivitySource.ActivityNames.SendRequest} {requestTypeName}",
                ActivityKind.Internal)
            : null;

        activity?.SetTag(MediatorActivitySource.Tags.RequestType, requestType.FullName);
        activity?.SetTag(MediatorActivitySource.Tags.ResponseType, typeof(TResponse).FullName);

        if (_options.EnableBuiltInLogging)
        {
            _logger.Log(_options.DefaultLogLevel, "Sending request {RequestType}", requestTypeName);
        }

        try
        {
            // Resolve behaviors via source-gen (no MakeGenericType needed)
            var behaviors = _sourceGeneratedMediator?.TryResolveBehaviors(
                _serviceProvider, requestType, typeof(TResponse));

            // Fallback: resolve behaviors from DI when source gen is not available
            behaviors ??= ResolveBehaviorsFromDI(requestType, typeof(TResponse));

            // Fast path: no behaviors - use direct dispatch
            if (behaviors.Count == 0)
            {
                // Try source-gen dispatch first
                var sourceGenResult = _sourceGeneratedMediator?.TrySendAsync<TResponse>(
                    _serviceProvider, request, cancellationToken);

                if (sourceGenResult.HasValue)
                {
                    var response = await sourceGenResult.Value;

                    if (_options.EnableBuiltInLogging)
                    {
                        _logger.Log(_options.DefaultLogLevel,
                            "Request {RequestType} handled successfully (source-generated)",
                            requestTypeName);
                    }

                    return response;
                }

                // DI fallback: resolve handler using reflection
                var fallbackResponse = InvokeHandlerFromDI<TResponse>(requestType, request, cancellationToken);
                if (fallbackResponse != null)
                {
                    var response = await fallbackResponse.Value;

                    if (_options.EnableBuiltInLogging)
                    {
                        _logger.Log(_options.DefaultLogLevel,
                            "Request {RequestType} handled successfully (DI fallback)",
                            requestTypeName);
                    }

                    return response;
                }

                throw new InvalidOperationException(
                    $"No handler registered for request type {requestType.FullName}. " +
                    $"Ensure a handler implementing IRequestHandler<{requestTypeName}, {typeof(TResponse).Name}> " +
                    "is registered via DI or AddGeneratedHandlers() is called.");
            }

            // Build and execute the pipeline with behaviors using source-gen dispatch
            var result = await ExecutePipeline(request, behaviors, cancellationToken);

            if (_options.EnableBuiltInLogging)
            {
                _logger.Log(_options.DefaultLogLevel, "Request {RequestType} handled successfully",
                    requestTypeName);
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

        using var activity = _options.EnableTracing
            ? MediatorActivitySource.Source.StartActivity(
                $"{MediatorActivitySource.ActivityNames.PublishNotification} {notificationTypeName}",
                ActivityKind.Internal)
            : null;

        activity?.SetTag(MediatorActivitySource.Tags.NotificationType, notificationType.FullName);

        if (_options.EnableBuiltInLogging)
        {
            _logger.Log(_options.DefaultLogLevel, "Publishing notification {NotificationType}",
                notificationTypeName);
        }

        // Get handlers from DI - no MakeGenericType needed since TNotification is compile-time resolved
        var handlers = _serviceProvider.GetServices<INotificationHandler<TNotification>>().ToList();

        activity?.SetTag(MediatorActivitySource.Tags.HandlerCount, handlers.Count);

        if (handlers.Count == 0)
        {
            if (_options.EnableBuiltInLogging)
            {
                _logger.Log(_options.DefaultLogLevel,
                    "No handlers registered for notification {NotificationType}", notificationTypeName);
            }
            return;
        }

        // Order handlers
        var orderedHandlers = OrderHandlers(handlers);

        // Get execution options
        var (executionStrategy, errorStrategy) = GetNotificationOptions(notificationType);

        activity?.SetTag(MediatorActivitySource.Tags.ExecutionStrategy, executionStrategy.ToString());

        try
        {
            await ExecuteNotificationHandlers(notification, orderedHandlers, executionStrategy,
                errorStrategy, cancellationToken);

            if (_options.EnableBuiltInLogging)
            {
                _logger.Log(_options.DefaultLogLevel,
                    "Notification {NotificationType} published to {HandlerCount} handlers",
                    notificationTypeName, handlers.Count);
            }
        }
        catch (Exception ex)
        {
            activity?.SetTag(MediatorActivitySource.Tags.Error, true);
            activity?.SetTag(MediatorActivitySource.Tags.ErrorMessage, ex.Message);

            if (_options.EnableBuiltInLogging)
            {
                _logger.LogError(ex, "Error publishing notification {NotificationType}",
                    notificationTypeName);
            }

            throw;
        }
    }

    /// <summary>
    /// Executes the pipeline with behaviors using source-generated typed dispatch.
    /// </summary>
    private async ValueTask<TResponse> ExecutePipeline<TResponse>(
        IRequest<TResponse> request,
        List<object> behaviors,
        CancellationToken cancellationToken)
    {
        var requestType = request.GetType();

        // Build the innermost handler delegate
        RequestHandlerDelegate<TResponse> handlerDelegate = () =>
        {
            // Try source-gen dispatch first
            var sourceGenResult = _sourceGeneratedMediator?.TryInvokeHandlerAsync<TResponse>(
                _serviceProvider, request, cancellationToken);

            if (sourceGenResult.HasValue)
            {
                return sourceGenResult.Value;
            }

            // DI fallback: resolve handler using reflection
            var fallbackResult = InvokeHandlerFromDI<TResponse>(requestType, request, cancellationToken);
            if (fallbackResult != null)
            {
                return fallbackResult.Value;
            }

            throw new InvalidOperationException(
                $"No handler registered for request type {requestType.FullName}.");
        };

        // Wrap with behaviors (in reverse order so first registered runs first)
        for (int i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var currentDelegate = handlerDelegate;

            handlerDelegate = () =>
                InvokeBehaviorAsync<TResponse>(behavior, requestType, request, currentDelegate,
                    cancellationToken);
        }

        return await handlerDelegate();
    }

    /// <summary>
    /// Invokes a behavior using source-generated typed dispatch (zero reflection).
    /// Falls back to reflection-based invocation for dynamically registered behaviors.
    /// </summary>
    [DebuggerStepThrough]
    private ValueTask<TResponse> InvokeBehaviorAsync<TResponse>(
        object behavior,
        Type requestType,
        object request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Try source-generated typed invocation first (zero reflection)
        if (_sourceGeneratedMediator != null)
        {
            try
            {
                return _sourceGeneratedMediator.InvokeBehavior<TResponse>(
                    requestType,
                    behavior.GetType(),
                    behavior,
                    request,
                    next,
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Behavior type not known at compile-time, fall through to reflection
            }
        }

        // Fallback: invoke behavior via reflection for dynamically registered behaviors
        var pipelineInterface = behavior.GetType()
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

        if (pipelineInterface != null)
        {
            var method = pipelineInterface.GetMethod("HandleAsync")!;
            try
            {
                return (ValueTask<TResponse>)method.Invoke(behavior, [request, next, cancellationToken])!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // Unreachable
            }
        }

        throw new InvalidOperationException(
            $"Cannot invoke behavior {behavior.GetType().Name} for request type {requestType.Name}. " +
            "Ensure the behavior implements IPipelineBehavior<,>.");
    }

    /// <summary>
    /// Resolves pipeline behaviors from DI using reflection as a fallback
    /// when the source generator is not available.
    /// </summary>
    private List<object> ResolveBehaviorsFromDI(Type requestType, Type responseType)
    {
        var behaviorInterfaceType = _behaviorTypeCache.GetOrAdd(
            (requestType, responseType),
            static key => typeof(IPipelineBehavior<,>).MakeGenericType(key.Item1, key.Item2));

        var enumerableType = typeof(IEnumerable<>).MakeGenericType(behaviorInterfaceType);
        var services = (System.Collections.IEnumerable)_serviceProvider.GetRequiredService(enumerableType);

        var behaviors = new List<object>();
        foreach (var service in services)
        {
            if (service != null) behaviors.Add(service);
        }
        return behaviors;
    }

    /// <summary>
    /// Attempts to invoke a handler resolved from DI using reflection.
    /// Returns null if no handler is registered.
    /// </summary>
    private ValueTask<TResponse>? InvokeHandlerFromDI<TResponse>(
        Type requestType, object request, CancellationToken cancellationToken)
    {
        var handlerInterfaceType = _handlerTypeCache.GetOrAdd(
            (requestType, typeof(TResponse)),
            static key => typeof(IRequestHandler<,>).MakeGenericType(key.Item1, key.Item2));

        var handler = _serviceProvider.GetService(handlerInterfaceType);
        if (handler == null) return null;

        var method = handlerInterfaceType.GetMethod("HandleAsync")!;
        try
        {
            return (ValueTask<TResponse>)method.Invoke(handler, [request, cancellationToken])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw; // Unreachable
        }
    }

    private List<INotificationHandler<TNotification>> OrderHandlers<TNotification>(
        List<INotificationHandler<TNotification>> handlers)
        where TNotification : INotification
    {
        if (handlers.Count <= 1)
            return handlers;

        // Check if any handler has a non-default order to avoid unnecessary sorting
        bool needsSort = false;
        for (int i = 0; i < handlers.Count; i++)
        {
            if (GetHandlerOrder(handlers[i].GetType()) != 0)
            {
                needsSort = true;
                break;
            }
        }

        if (!needsSort)
            return handlers;

        return [.. handlers.OrderBy(h => GetHandlerOrder(h.GetType()))];
    }

    private int GetHandlerOrder(Type handlerType)
    {
        // Try source-generated order first
        var sourceGenOrder = _sourceGeneratedMediator?.TryGetHandlerOrder(handlerType);
        if (sourceGenOrder.HasValue)
        {
            return sourceGenOrder.Value;
        }

        // Fallback to cached attribute reflection for dynamically registered handlers
        return _handlerOrderCache.GetOrAdd(handlerType, static type =>
        {
            var orderAttr = type.GetCustomAttribute<NotificationHandlerOrderAttribute>();
            return orderAttr?.Order ?? 0;
        });
    }

    private (NotificationExecutionStrategy, NotificationErrorStrategy) GetNotificationOptions(
        Type notificationType)
    {
        // Try source-generated options first
        var cachedOptions = _sourceGeneratedMediator?.TryGetNotificationOptions(notificationType);
        if (cachedOptions.HasValue)
        {
            return cachedOptions.Value;
        }

        // Fallback to cached attribute reflection for dynamically defined notifications
        var attrResult = _notificationOptionsCache.GetOrAdd(notificationType, type =>
        {
            var attr = type.GetCustomAttribute<NotificationOptionsAttribute>();
            if (attr != null && attr.OverrideGlobal)
            {
                return (attr.ExecutionStrategy, attr.ErrorStrategy);
            }
            return null;
        });

        return attrResult ?? (_options.NotificationExecutionStrategy, _options.NotificationErrorStrategy);
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
                await ExecuteParallel(notification, handlers, cancellationToken);
                break;

            case NotificationExecutionStrategy.StopOnFirst:
                await ExecuteStopOnFirst(notification, handlers, errorStrategy, cancellationToken);
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
            List<Exception>? exceptions = null;

            try
            {
                await Task.WhenAll(tasksSpan);
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
        finally
        {
            Array.Clear(rentedArray, 0, count);
            ArrayPool<Task>.Shared.Return(rentedArray);
        }
    }

    private static async ValueTask ExecuteStopOnFirst<TNotification>(
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
                return; // Success - stop here
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
                $"All notification handlers for {typeof(TNotification).Name} threw exceptions.",
                exceptions);
        }
    }
}
