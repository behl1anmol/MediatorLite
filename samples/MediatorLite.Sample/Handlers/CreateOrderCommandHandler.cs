using MediatorLite;
using MediatorLite.Sample.Requests;
using Microsoft.Extensions.Logging;

namespace MediatorLite.Sample.Handlers;

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, int>
{
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(ILogger<CreateOrderCommandHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<int> HandleAsync(CreateOrderCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating order: {Product} x {Quantity} @ {Price:C}",
            request.ProductName, request.Quantity, request.Price);

        // Simulate order creation returning new order ID
        var orderId = Random.Shared.Next(1000, 9999);
        return ValueTask.FromResult(orderId);
    }
}
