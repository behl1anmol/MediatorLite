namespace MediatorLite.Sample.SourceGen.Notifications;

/// <summary>
/// Notification published when an order is placed.
/// </summary>
public sealed record OrderPlacedNotification(
    string OrderId,
    int ProductId,
    int Quantity,
    string CustomerEmail,
    decimal TotalAmount) : INotification;
