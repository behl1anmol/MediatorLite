using Microsoft.Extensions.Logging;

namespace MediatorLite.Sample.SourceGen.Notifications;

/// <summary>
/// Handler that creates an audit log entry for orders.
/// </summary>
[NotificationHandlerOrder(2)]
public sealed class OrderAuditLogHandler : INotificationHandler<OrderPlacedNotification>
{
    private readonly ILogger<OrderAuditLogHandler> _logger;

    public OrderAuditLogHandler(ILogger<OrderAuditLogHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask HandleAsync(OrderPlacedNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📝 Audit log: Order {OrderId} created for product {ProductId}, quantity {Quantity}, amount {TotalAmount:C}",
            notification.OrderId,
            notification.ProductId,
            notification.Quantity,
            notification.TotalAmount);

        return ValueTask.CompletedTask;
    }
}
