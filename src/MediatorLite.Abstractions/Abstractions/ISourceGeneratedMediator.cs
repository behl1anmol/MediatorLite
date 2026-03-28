namespace MediatorLite;

/// <summary>
/// Interface for source-generated mediator dispatch functionality.
/// Implementations are generated at compile-time to avoid runtime reflection.
/// </summary>
/// <remarks>
/// This interface enables the mediator to dispatch requests using compile-time
/// generated code instead of runtime reflection. The source generator creates
/// an implementation that uses pattern matching to route requests to handlers.
/// </remarks>
public interface ISourceGeneratedMediator
{
    /// <summary>
    /// Attempts to dispatch a request using compile-time generated code.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="serviceProvider">The service provider for resolving handlers.</param>
    /// <param name="request">The request to dispatch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResponse}"/> if the request type was discovered at compile-time;
    /// otherwise, null indicating the caller should fall back to reflection-based dispatch.
    /// </returns>
    ValueTask<TResponse>? TrySendAsync<TResponse>(
        IServiceProvider serviceProvider,
        IRequest<TResponse> request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to invoke a request handler directly using compile-time generated code.
    /// This is used when behaviors are present - source-gen handles the innermost handler call.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="serviceProvider">The service provider for resolving handlers.</param>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResponse}"/> if the request type was discovered at compile-time;
    /// otherwise, null indicating the caller should fall back to reflection-based invocation.
    /// </returns>
    ValueTask<TResponse>? TryInvokeHandlerAsync<TResponse>(
        IServiceProvider serviceProvider,
        IRequest<TResponse> request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the execution order for a notification handler type.
    /// </summary>
    /// <param name="handlerType">The handler type to get the order for.</param>
    /// <returns>
    /// The order value if the handler was discovered at compile-time and has an order attribute;
    /// otherwise, null indicating the caller should check at runtime.
    /// </returns>
    int? TryGetHandlerOrder(Type handlerType);

    /// <summary>
    /// Gets the notification options for a notification type.
    /// </summary>
    /// <param name="notificationType">The notification type to get options for.</param>
    /// <returns>
    /// A tuple of (ExecutionStrategy, ErrorStrategy) if the notification was discovered at compile-time
    /// and has options configured; otherwise, null indicating the caller should use default options.
    /// </returns>
    (NotificationExecutionStrategy ExecutionStrategy, NotificationErrorStrategy ErrorStrategy)? TryGetNotificationOptions(Type notificationType);

    /// <summary>
    /// Attempts to get cached notification handlers for the given notification type.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="serviceProvider">The service provider for resolving handlers.</param>
    /// <returns>
    /// A read-only list of handlers if discovered at compile-time; otherwise, null indicating
    /// the caller should fall back to GetServices() enumeration.
    /// </returns>
    IReadOnlyList<INotificationHandler<TNotification>>? TryGetCachedHandlers<TNotification>(
        IServiceProvider serviceProvider)
        where TNotification : INotification;

    /// <summary>
    /// Resolves pipeline behaviors for a request type using compile-time generated typed resolution.
    /// Eliminates the need for MakeGenericType at runtime.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving behaviors.</param>
    /// <param name="requestType">The concrete request type.</param>
    /// <param name="responseType">The response type.</param>
    /// <returns>
    /// A list of behavior instances if the request type was discovered at compile-time;
    /// otherwise, null indicating the caller should fall back to runtime resolution.
    /// </returns>
    List<object>? TryResolveBehaviors(
        IServiceProvider serviceProvider,
        Type requestType,
        Type responseType);

    /// <summary>
    /// Invokes a request handler using generated typed code (zero reflection).
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="requestType">The concrete request type.</param>
    /// <param name="handler">The handler instance (will be cast to correct interface).</param>
    /// <param name="request">The request instance (will be cast to correct type).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The handler response.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the request type was not discovered at compile-time.
    /// </exception>
    ValueTask<TResponse> InvokeHandler<TResponse>(
        Type requestType,
        object handler,
        object request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invokes a pipeline behavior using generated typed code (zero reflection).
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="requestType">The concrete request type.</param>
    /// <param name="behaviorType">The concrete behavior type.</param>
    /// <param name="behavior">The behavior instance (will be cast to correct interface).</param>
    /// <param name="request">The request instance (will be cast to correct type).</param>
    /// <param name="next">The next delegate in the pipeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The behavior response.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the request type or behavior type was not discovered at compile-time.
    /// </exception>
    ValueTask<TResponse> InvokeBehavior<TResponse>(
        Type requestType,
        Type behaviorType,
        object behavior,
        object request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
