namespace MediatorLite;

/// <summary>
/// Main interface for sending requests and publishing notifications through the mediator.
/// </summary>
/// <remarks>
/// The mediator acts as a dispatcher that routes requests to their handlers
/// and publishes notifications to all registered handlers.
/// <para>
/// The public API uses <see cref="Task{TResult}"/> for maximum consumer ergonomics,
/// enabling natural parallel execution patterns like <c>Task.WhenAll</c>. Internally,
/// handlers use <see cref="ValueTask{TResult}"/> for performance on synchronous paths.
/// </para>
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
///     // Parallel execution is natural with Task-based API
///     public async Task&lt;(User, Order)&gt; GetUserAndOrderAsync(int userId, int orderId, CancellationToken ct)
///     {
///         var userTask = _mediator.SendAsync(new GetUserQuery(userId), ct);
///         var orderTask = _mediator.SendAsync(new GetOrderQuery(orderId), ct);
///         await Task.WhenAll(userTask, orderTask);
///         return (userTask.Result, orderTask.Result);
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
    /// <returns>A <see cref="Task{TResponse}"/> representing the response from the handler.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the request type.</exception>
    /// <remarks>
    /// Returns <see cref="Task{TResponse}"/> for consumer ergonomics, enabling parallel patterns.
    /// Handlers internally use <see cref="ValueTask{TResponse}"/> for synchronous completion optimization.
    /// </remarks>
    Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification to all registered handlers.
    /// </summary>
    /// <typeparam name="TNotification">The type of notification to publish.</typeparam>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// Returns <see cref="Task"/> for consumer ergonomics.
    /// Notification handlers internally use <see cref="ValueTask"/> for synchronous completion optimization.
    /// </remarks>
    Task PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
