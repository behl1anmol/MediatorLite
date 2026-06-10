namespace MediatorLite;

/// <summary>
/// Main interface for sending requests and publishing notifications through the mediator.
/// </summary>
/// <remarks>
/// The mediator acts as a dispatcher that routes requests to their handlers
/// and publishes notifications to all registered handlers.
/// <para>
/// The public API uses <see cref="ValueTask{TResult}"/> end-to-end so that synchronously
/// completing handlers incur zero heap allocations. A <see cref="ValueTask{TResult}"/> must be
/// consumed exactly once (typically by awaiting it directly). If you need to fan out with
/// <c>Task.WhenAll</c>, store multiple results, or await more than once, convert it first with
/// <c>.AsTask()</c>.
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
///     // For parallel composition, materialize the ValueTasks with AsTask() first
///     public async Task&lt;(User, Order)&gt; GetUserAndOrderAsync(int userId, int orderId, CancellationToken ct)
///     {
///         var userTask = _mediator.SendAsync(new GetUserQuery(userId), ct).AsTask();
///         var orderTask = _mediator.SendAsync(new GetOrderQuery(orderId), ct).AsTask();
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
    /// <returns>A <see cref="ValueTask{TResponse}"/> representing the response from the handler.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the request type.</exception>
    /// <remarks>
    /// Returns <see cref="ValueTask{TResponse}"/> for zero-allocation dispatch on synchronous paths.
    /// Consume the result exactly once; use <c>.AsTask()</c> when a <see cref="Task{TResponse}"/> is required.
    /// </remarks>
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
    /// <remarks>
    /// Returns <see cref="ValueTask"/> for zero-allocation dispatch on synchronous paths.
    /// Consume the result exactly once; use <c>.AsTask()</c> when a <see cref="Task"/> is required.
    /// </remarks>
    ValueTask PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
