namespace MediatorLite;

/// <summary>
/// Marker interface for notifications that can be published to multiple handlers.
/// </summary>
/// <remarks>
/// Unlike requests, notifications can have zero or more handlers.
/// All handlers are invoked when a notification is published.
/// </remarks>
/// <example>
/// <code>
/// public record UserCreatedNotification(int UserId, string Email) : INotification;
/// </code>
/// </example>
public interface INotification;
