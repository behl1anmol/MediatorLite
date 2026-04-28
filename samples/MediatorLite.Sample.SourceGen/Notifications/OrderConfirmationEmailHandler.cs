using Microsoft.Extensions.Logging;

namespace MediatorLite.Sample.SourceGen.Notifications;

/// <summary>
/// Handler that sends order confirmation email.
/// </summary>
[NotificationHandlerOrder(1)]
public sealed class OrderConfirmationEmailHandler : INotificationHandler<OrderPlacedNotification>
{
    private readonly ILogger<OrderConfirmationEmailHandler> _logger;

    public OrderConfirmationEmailHandler(ILogger<OrderConfirmationEmailHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask HandleAsync(OrderPlacedNotification notification, CancellationToken cancellationToken = default)
    {
        // Simulate sending email
        await Task.Delay(100, cancellationToken);

        _logger.LogInformation(
            "📧 Sent order confirmation email to {Email} for order {OrderId}. Total: {TotalAmount:C}",
            MaskEmail(notification.CustomerEmail),
            notification.OrderId,
            notification.TotalAmount);
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
