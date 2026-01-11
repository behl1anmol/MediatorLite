using Microsoft.Extensions.Logging;
using MediatorLite.Sample.SourceGen.Requests;

namespace MediatorLite.Sample.SourceGen.Handlers;

/// <summary>
/// Handler for PlaceOrderCommand - processes order placement.
/// </summary>
public sealed class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, OrderResult>
{
    private readonly ILogger<PlaceOrderCommandHandler> _logger;
    private readonly IMediator _mediator;

    public PlaceOrderCommandHandler(ILogger<PlaceOrderCommandHandler> logger, IMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }

    public async ValueTask<OrderResult> HandleAsync(PlaceOrderCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing order for ProductId: {ProductId}, Quantity: {Quantity}", 
            request.ProductId, request.Quantity);

        // Get product to calculate price
        var product = await _mediator.SendAsync(new GetProductQuery(request.ProductId), cancellationToken);

        // Calculate total
        var totalAmount = product.Price * request.Quantity;
        var orderId = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";

        // Update stock
        await _mediator.SendAsync(new UpdateStockCommand(request.ProductId, -request.Quantity), cancellationToken);

        // Publish order placed notification
        await _mediator.PublishAsync(
            new Notifications.OrderPlacedNotification(orderId, request.ProductId, request.Quantity, request.CustomerEmail, totalAmount), 
            cancellationToken);

        _logger.LogInformation("Order {OrderId} placed successfully. Total: {TotalAmount:C}", orderId, totalAmount);

        return new OrderResult(orderId, totalAmount, DateTime.UtcNow);
    }
}
