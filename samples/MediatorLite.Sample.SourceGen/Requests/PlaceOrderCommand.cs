namespace MediatorLite.Sample.SourceGen.Requests;

/// <summary>
/// Command to place an order.
/// </summary>
public sealed record PlaceOrderCommand(int ProductId, int Quantity, string CustomerEmail) : IRequest<OrderResult>;

/// <summary>
/// Result of placing an order.
/// </summary>
public sealed record OrderResult(string OrderId, decimal TotalAmount, DateTime OrderDate);
