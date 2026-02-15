using MediatorLite;
using MediatorLite.Sample.Notifications;
using Microsoft.Extensions.Logging;

namespace MediatorLite.Sample.Notifications;

[NotificationHandlerOrder(1)]
public class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification>
{
    private readonly ILogger<SendWelcomeEmailHandler> _logger;

    public SendWelcomeEmailHandler(ILogger<SendWelcomeEmailHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("   Sending welcome email to {Email}", notification.Email);
        return ValueTask.CompletedTask;
    }
}
