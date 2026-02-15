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
            notification.CustomerEmail,
            notification.OrderId,
            notification.TotalAmount);
    }
}
