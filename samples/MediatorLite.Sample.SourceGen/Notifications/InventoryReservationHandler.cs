using Microsoft.Extensions.Logging;

namespace MediatorLite.Sample.SourceGen.Notifications;

/// <summary>
/// Handler that notifies inventory system about the order.
/// </summary>
[NotificationHandlerOrder(3)]
public sealed class InventoryReservationHandler : INotificationHandler<OrderPlacedNotification>
{
    private readonly ILogger<InventoryReservationHandler> _logger;

    public InventoryReservationHandler(ILogger<InventoryReservationHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask HandleAsync(OrderPlacedNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📦 Inventory reservation created: {Quantity} units of product {ProductId} reserved for order {OrderId}",
            notification.Quantity,
            notification.ProductId,
            notification.OrderId);

        return ValueTask.CompletedTask;
    }
}
