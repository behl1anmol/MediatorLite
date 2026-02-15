using Microsoft.Extensions.Logging;
using MediatorLite.Sample.SourceGen.Requests;

namespace MediatorLite.Sample.SourceGen.Handlers;

/// <summary>
/// Handler for UpdateStockCommand - updates product stock quantity.
/// This is a void command handler (no return value).
/// </summary>
public sealed class UpdateStockCommandHandler : IRequestHandler<UpdateStockCommand>
{
    private readonly ILogger<UpdateStockCommandHandler> _logger;

    public UpdateStockCommandHandler(ILogger<UpdateStockCommandHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask HandleAsync(UpdateStockCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating stock for ProductId: {ProductId}, Change: {QuantityChange}",
            request.ProductId, request.QuantityChange);

        // Simulate stock update (in real app, would update database)
        if (request.QuantityChange < 0)
        {
            _logger.LogDebug("Stock decreased by {Quantity} units", Math.Abs(request.QuantityChange));
        }
        else
        {
            _logger.LogDebug("Stock increased by {Quantity} units", request.QuantityChange);
        }

        return ValueTask.CompletedTask;
    }
}
