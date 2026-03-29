using System.ComponentModel.DataAnnotations;
using ML = global::MediatorLite;
using MR = global::MediatR;

namespace MediatorLite.RestApiBenchmarks.Application.Contracts;

public sealed record CreateOrderCommand(
    [property: Range(1, int.MaxValue)] int CustomerId,
    [property: MinLength(1)] IReadOnlyList<CreateOrderLineInput> Lines,
    [property: StringLength(64, MinimumLength = 3)] string CorrelationId)
    : ML.IRequest<CreateOrderResult>, MR.IRequest<CreateOrderResult>;

public sealed record GetOrderDetailsQuery(
    [property: Range(1, int.MaxValue)] int OrderId)
    : ML.IRequest<OrderDetailsDto>, MR.IRequest<OrderDetailsDto>;

public sealed record SearchOrdersQuery(
    [property: Range(1, int.MaxValue)] int CustomerId,
    [property: Range(1, 100)] int Page,
    [property: Range(1, 200)] int PageSize)
    : ML.IRequest<IReadOnlyList<OrderListItemDto>>, MR.IRequest<IReadOnlyList<OrderListItemDto>>;

public sealed record GetSalesReportQuery(
    [property: Range(1, 365)] int Days)
    : ML.IRequest<SalesReportDto>, MR.IRequest<SalesReportDto>;

public sealed record CancelOrderCommand(
    [property: Range(1, int.MaxValue)] int OrderId,
    [property: StringLength(128, MinimumLength = 3)] string Reason)
    : ML.IRequest<CancelOrderResult>, MR.IRequest<CancelOrderResult>;

public sealed record UpdateOrderStatusCommand(
    [property: Range(1, int.MaxValue)] int OrderId,
    [property: StringLength(32, MinimumLength = 3)] string ExpectedCurrentStatus,
    [property: StringLength(32, MinimumLength = 3)] string NewStatus)
    : ML.IRequest<UpdateOrderStatusResult>, MR.IRequest<UpdateOrderStatusResult>;

public sealed record GetCustomerOrderSummaryQuery(
    [property: Range(1, int.MaxValue)] int CustomerId,
    [property: Range(1, 365)] int Days)
    : ML.IRequest<CustomerOrderSummaryDto>, MR.IRequest<CustomerOrderSummaryDto>;

public sealed record OrderCreatedNotification(int OrderId, int CustomerId, decimal TotalAmount)
    : ML.INotification, MR.INotification;
