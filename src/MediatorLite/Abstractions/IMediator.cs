namespace MediatorLite;

/// <summary>
/// Main interface for sending requests and publishing notifications through the mediator.
/// </summary>
/// <remarks>
/// The mediator acts as a dispatcher that routes requests to their handlers
/// and publishes notifications to all registered handlers.
/// </remarks>
/// <example>
/// <code>
/// public class MyService
/// {
///     private readonly IMediator _mediator;
///     
///     public MyService(IMediator mediator)
///     {
///         _mediator = mediator;
///     }
///     
///     public async Task&lt;User&gt; GetUserAsync(int id, CancellationToken ct)
///     {
///         return await _mediator.SendAsync(new GetUserQuery(id), ct);
///     }
///     
///     public async Task NotifyUserCreatedAsync(int userId, CancellationToken ct)
///     {
///         await _mediator.PublishAsync(new UserCreatedNotification(userId), ct);
///     }
/// }
/// </code>
/// </example>
public interface IMediator
{
    /// <summary>
    /// Sends a request to a single handler and returns the response.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from the handler.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ValueTask{TResponse}"/> representing the response from the handler.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the request type.</exception>
    ValueTask<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification to all registered handlers.
    /// </summary>
    /// <typeparam name="TNotification">The type of notification to publish.</typeparam>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
