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
}
