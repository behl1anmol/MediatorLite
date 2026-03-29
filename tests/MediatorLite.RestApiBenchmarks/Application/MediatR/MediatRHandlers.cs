using MediatorLite.RestApiBenchmarks.Application.Common;
using MediatorLite.RestApiBenchmarks.Application.Contracts;
using MR = global::MediatR;

namespace MediatorLite.RestApiBenchmarks.Application.MediatR;

public sealed class MediatRCreateOrderHandler : MR.IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    private readonly OrderApplicationService _applicationService;

    public MediatRCreateOrderHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async Task<CreateOrderResult> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        return await _applicationService.CreateOrderAsync(request, cancellationToken);
    }
}

public sealed class MediatRGetOrderDetailsHandler : MR.IRequestHandler<GetOrderDetailsQuery, OrderDetailsDto>
{
    private readonly OrderApplicationService _applicationService;

    public MediatRGetOrderDetailsHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async Task<OrderDetailsDto> Handle(GetOrderDetailsQuery request, CancellationToken cancellationToken)
    {
        return await _applicationService.GetOrderDetailsAsync(request, cancellationToken);
    }
}

public sealed class MediatRSearchOrdersHandler : MR.IRequestHandler<SearchOrdersQuery, IReadOnlyList<OrderListItemDto>>
{
    private readonly OrderApplicationService _applicationService;

    public MediatRSearchOrdersHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async Task<IReadOnlyList<OrderListItemDto>> Handle(SearchOrdersQuery request, CancellationToken cancellationToken)
    {
        return await _applicationService.SearchOrdersAsync(request, cancellationToken);
    }
}

public sealed class MediatRGetSalesReportHandler : MR.IRequestHandler<GetSalesReportQuery, SalesReportDto>
{
    private readonly OrderApplicationService _applicationService;

    public MediatRGetSalesReportHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async Task<SalesReportDto> Handle(GetSalesReportQuery request, CancellationToken cancellationToken)
    {
        return await _applicationService.GetSalesReportAsync(request, cancellationToken);
    }
}

public sealed class MediatRCancelOrderHandler : MR.IRequestHandler<CancelOrderCommand, CancelOrderResult>
{
    private readonly OrderApplicationService _applicationService;

    public MediatRCancelOrderHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async Task<CancelOrderResult> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        return await _applicationService.CancelOrderAsync(request, cancellationToken);
    }
}

public sealed class MediatRUpdateOrderStatusHandler : MR.IRequestHandler<UpdateOrderStatusCommand, UpdateOrderStatusResult>
{
    private readonly OrderApplicationService _applicationService;

    public MediatRUpdateOrderStatusHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async Task<UpdateOrderStatusResult> Handle(UpdateOrderStatusCommand request, CancellationToken cancellationToken)
    {
        return await _applicationService.UpdateOrderStatusAsync(request, cancellationToken);
    }
}

public sealed class MediatRCustomerSummaryHandler : MR.IRequestHandler<GetCustomerOrderSummaryQuery, CustomerOrderSummaryDto>
{
    private readonly OrderApplicationService _applicationService;

    public MediatRCustomerSummaryHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async Task<CustomerOrderSummaryDto> Handle(GetCustomerOrderSummaryQuery request, CancellationToken cancellationToken)
    {
        return await _applicationService.GetCustomerSummaryAsync(request, cancellationToken);
    }
}

public sealed class MediatROrderAuditNotificationHandler : MR.INotificationHandler<OrderCreatedNotification>
{
    private readonly OrderApplicationService _applicationService;

    public MediatROrderAuditNotificationHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async Task Handle(OrderCreatedNotification notification, CancellationToken cancellationToken)
    {
        var payload = $"{{\"orderId\":{notification.OrderId},\"customerId\":{notification.CustomerId}}}";
        await _applicationService.WriteAuditAsync("ORDER_CREATED_NOTIFICATION", payload, cancellationToken);
    }
}

public sealed class MediatROrderFinanceNotificationHandler : MR.INotificationHandler<OrderCreatedNotification>
{
    private readonly OrderApplicationService _applicationService;

    public MediatROrderFinanceNotificationHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async Task Handle(OrderCreatedNotification notification, CancellationToken cancellationToken)
    {
        var payload = $"{{\"orderId\":{notification.OrderId},\"amount\":{notification.TotalAmount}}}";
        await _applicationService.WriteAuditAsync("ORDER_FINANCE_ENQUEUED", payload, cancellationToken);
    }
}

public sealed class MediatROrderFulfillmentNotificationHandler : MR.INotificationHandler<OrderCreatedNotification>
{
    private readonly OrderApplicationService _applicationService;

    public MediatROrderFulfillmentNotificationHandler(OrderApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public async Task Handle(OrderCreatedNotification notification, CancellationToken cancellationToken)
    {
        var payload = $"{{\"orderId\":{notification.OrderId},\"stage\":\"fulfillment\"}}";
        await _applicationService.WriteAuditAsync("ORDER_FULFILLMENT_REQUESTED", payload, cancellationToken);
    }
}
