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
        _logger.LogInformation("   Sending welcome email to {Email}", MaskEmail(notification.Email));
        return ValueTask.CompletedTask;
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return email;
        var parts = email.Split('@');
        if (parts.Length != 2) return "***";

        var username = parts[0];
        var domain = parts[1];

        if (username.Length <= 1) return $"*@{domain}";

        return $"{username[0]}***@{domain}";
    }
}
