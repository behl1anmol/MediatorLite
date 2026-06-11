using ML = global::MediatorLite;
using MR = global::MediatR;

namespace MediatorLite.RestApiBenchmarks.Application.Contracts;

// NOTE: these shared contracts intentionally carry no DataAnnotations attributes.
// MediatorLite's source generator auto-wires DataAnnotationsValidator<T> +
// ValidationBehavior<,> for annotated request types, which would add a validation
// behavior to every MediatorLite pipeline that the MediatR stack does not run —
// a parity violation the BenchmarkParityGuard rejects. Both stacks validate
// CreateOrderCommand through the shared IAppValidator<T> inside their respective
// validation behaviors instead.

public sealed record CreateOrderCommand(
    int CustomerId,
    IReadOnlyList<CreateOrderLineInput> Lines,
    string CorrelationId)
    : ML.IRequest<CreateOrderResult>, MR.IRequest<CreateOrderResult>;

public sealed record GetOrderDetailsQuery(
    int OrderId)
    : ML.IRequest<OrderDetailsDto>, MR.IRequest<OrderDetailsDto>;

public sealed record SearchOrdersQuery(
    int CustomerId,
    int Page,
    int PageSize)
    : ML.IRequest<IReadOnlyList<OrderListItemDto>>, MR.IRequest<IReadOnlyList<OrderListItemDto>>;

public sealed record GetSalesReportQuery(
    int Days)
    : ML.IRequest<SalesReportDto>, MR.IRequest<SalesReportDto>;

public sealed record CancelOrderCommand(
    int OrderId,
    string Reason)
    : ML.IRequest<CancelOrderResult>, MR.IRequest<CancelOrderResult>;

public sealed record UpdateOrderStatusCommand(
    int OrderId,
    string ExpectedCurrentStatus,
    string NewStatus)
    : ML.IRequest<UpdateOrderStatusResult>, MR.IRequest<UpdateOrderStatusResult>;

public sealed record GetCustomerOrderSummaryQuery(
    int CustomerId,
    int Days)
    : ML.IRequest<CustomerOrderSummaryDto>, MR.IRequest<CustomerOrderSummaryDto>;

public sealed record OrderCreatedNotification(int OrderId, int CustomerId, decimal TotalAmount)
    : ML.INotification, MR.INotification;
