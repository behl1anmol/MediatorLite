using MediatorLite;
using MediatorLite.Sample.Notifications;
using Microsoft.Extensions.Logging;

namespace MediatorLite.Sample.Notifications;

[NotificationHandlerOrder(2)]
public class CreateAuditLogHandler : INotificationHandler<UserCreatedNotification>
{
    private readonly ILogger<CreateAuditLogHandler> _logger;

    public CreateAuditLogHandler(ILogger<CreateAuditLogHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("   Creating audit log for user {UserId}", notification.UserId);
        return ValueTask.CompletedTask;
    }
}
