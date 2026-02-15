using MediatorLite;

namespace MediatorLite.Sample.Notifications;

public record UserCreatedNotification(int UserId, string Email) : INotification;
