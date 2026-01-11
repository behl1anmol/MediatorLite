namespace MediatorLite;

/// <summary>
/// Defines a handler for notifications of type <typeparamref name="TNotification"/>.
/// </summary>
/// <typeparam name="TNotification">The type of notification being handled.</typeparam>
/// <remarks>
/// Multiple handlers can be registered for the same notification type.
/// All handlers are invoked when a notification is published.
/// The execution order can be controlled via the <see cref="NotificationHandlerOrderAttribute"/>.
/// </remarks>
/// <example>
/// <code>
/// public class SendWelcomeEmailHandler : INotificationHandler&lt;UserCreatedNotification&gt;
/// {
///     public async ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken cancellationToken = default)
///     {
///         await _emailService.SendWelcomeEmailAsync(notification.Email, cancellationToken);
///     }
/// }
/// </code>
/// </example>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Handles a notification asynchronously.
    /// </summary>
    /// <param name="notification">The notification to handle.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask HandleAsync(TNotification notification, CancellationToken cancellationToken = default);
}
