using MediatorLite.RestApiBenchmarks.Application.Common;
using MediatorLite.RestApiBenchmarks.Application.Contracts;
using ML = global::MediatorLite;

namespace MediatorLite.RestApiBenchmarks.Application.MediatorLite;

public sealed class MediatorLiteCreateOrderHandler : ML.IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    private readonly OrderApplicationService _applicationService;

    public MediatorLiteCreateOrderHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async ValueTask<CreateOrderResult> HandleAsync(CreateOrderCommand request, CancellationToken cancellationToken = default)
    {
        return await _applicationService.CreateOrderAsync(request, cancellationToken);
    }
}

public sealed class MediatorLiteGetOrderDetailsHandler : ML.IRequestHandler<GetOrderDetailsQuery, OrderDetailsDto>
{
    private readonly OrderApplicationService _applicationService;

    public MediatorLiteGetOrderDetailsHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async ValueTask<OrderDetailsDto> HandleAsync(GetOrderDetailsQuery request, CancellationToken cancellationToken = default)
    {
        return await _applicationService.GetOrderDetailsAsync(request, cancellationToken);
    }
}

public sealed class MediatorLiteSearchOrdersHandler : ML.IRequestHandler<SearchOrdersQuery, IReadOnlyList<OrderListItemDto>>
{
    private readonly OrderApplicationService _applicationService;

    public MediatorLiteSearchOrdersHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async ValueTask<IReadOnlyList<OrderListItemDto>> HandleAsync(SearchOrdersQuery request, CancellationToken cancellationToken = default)
    {
        return await _applicationService.SearchOrdersAsync(request, cancellationToken);
    }
}

public sealed class MediatorLiteGetSalesReportHandler : ML.IRequestHandler<GetSalesReportQuery, SalesReportDto>
{
    private readonly OrderApplicationService _applicationService;

    public MediatorLiteGetSalesReportHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async ValueTask<SalesReportDto> HandleAsync(GetSalesReportQuery request, CancellationToken cancellationToken = default)
    {
        return await _applicationService.GetSalesReportAsync(request, cancellationToken);
    }
}

public sealed class MediatorLiteCancelOrderHandler : ML.IRequestHandler<CancelOrderCommand, CancelOrderResult>
{
    private readonly OrderApplicationService _applicationService;

    public MediatorLiteCancelOrderHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async ValueTask<CancelOrderResult> HandleAsync(CancelOrderCommand request, CancellationToken cancellationToken = default)
    {
        return await _applicationService.CancelOrderAsync(request, cancellationToken);
    }
}

public sealed class MediatorLiteUpdateOrderStatusHandler : ML.IRequestHandler<UpdateOrderStatusCommand, UpdateOrderStatusResult>
{
    private readonly OrderApplicationService _applicationService;

    public MediatorLiteUpdateOrderStatusHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async ValueTask<UpdateOrderStatusResult> HandleAsync(UpdateOrderStatusCommand request, CancellationToken cancellationToken = default)
    {
        return await _applicationService.UpdateOrderStatusAsync(request, cancellationToken);
    }
}

public sealed class MediatorLiteCustomerSummaryHandler : ML.IRequestHandler<GetCustomerOrderSummaryQuery, CustomerOrderSummaryDto>
{
    private readonly OrderApplicationService _applicationService;

    public MediatorLiteCustomerSummaryHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async ValueTask<CustomerOrderSummaryDto> HandleAsync(GetCustomerOrderSummaryQuery request, CancellationToken cancellationToken = default)
    {
        return await _applicationService.GetCustomerSummaryAsync(request, cancellationToken);
    }
}

[ML.NotificationHandlerOrder(1)]
public sealed class MediatorLiteOrderAuditNotificationHandler : ML.INotificationHandler<OrderCreatedNotification>
{
    private readonly OrderApplicationService _applicationService;

    public MediatorLiteOrderAuditNotificationHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async ValueTask HandleAsync(OrderCreatedNotification notification, CancellationToken cancellationToken = default)
    {
        var payload = $"{{\"orderId\":{notification.OrderId},\"customerId\":{notification.CustomerId}}}";
        await _applicationService.WriteAuditAsync("ORDER_CREATED_NOTIFICATION", payload, cancellationToken);
    }
}

[ML.NotificationHandlerOrder(2)]
public sealed class MediatorLiteOrderFinanceNotificationHandler : ML.INotificationHandler<OrderCreatedNotification>
{
    private readonly OrderApplicationService _applicationService;

    public MediatorLiteOrderFinanceNotificationHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async ValueTask HandleAsync(OrderCreatedNotification notification, CancellationToken cancellationToken = default)
    {
        var payload = $"{{\"orderId\":{notification.OrderId},\"amount\":{notification.TotalAmount}}}";
        await _applicationService.WriteAuditAsync("ORDER_FINANCE_ENQUEUED", payload, cancellationToken);
    }
}

[ML.NotificationHandlerOrder(3)]
public sealed class MediatorLiteOrderFulfillmentNotificationHandler : ML.INotificationHandler<OrderCreatedNotification>
{
    private readonly OrderApplicationService _applicationService;

    public MediatorLiteOrderFulfillmentNotificationHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async ValueTask HandleAsync(OrderCreatedNotification notification, CancellationToken cancellationToken = default)
    {
        var payload = $"{{\"orderId\":{notification.OrderId},\"stage\":\"fulfillment\"}}";
        await _applicationService.WriteAuditAsync("ORDER_FULFILLMENT_REQUESTED", payload, cancellationToken);
    }
}
