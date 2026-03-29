namespace MediatorLite.RestApiBenchmarks.Application.Contracts;

public sealed record CreateOrderLineInput(int ProductId, int Quantity);

public sealed record CreateOrderResult(int OrderId, decimal TotalAmount, string Status);

public sealed record CancelOrderResult(int OrderId, string Status, DateTime CancelledAtUtc);

public sealed record UpdateOrderStatusResult(int OrderId, string PreviousStatus, string NewStatus, DateTime UpdatedAtUtc);

public sealed record OrderDetailsDto(
    int OrderId,
    int CustomerId,
    string CustomerName,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    IReadOnlyList<OrderLineDto> Lines);

public sealed record OrderLineDto(int ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record OrderListItemDto(int OrderId, int CustomerId, string Status, decimal TotalAmount, DateTime CreatedAtUtc);

public sealed record SalesReportDto(int OrderCount, decimal GrossRevenue, decimal AverageOrderValue);

public sealed record CustomerOrderSummaryDto(
    int CustomerId,
    int OrderCount,
    decimal TotalSpent,
    DateTime? LastOrderAtUtc,
    int PendingOrderCount,
    int CancelledOrderCount);
