using Microsoft.Extensions.Logging;
using MediatorLite.Sample.SourceGen.Requests;

namespace MediatorLite.Sample.SourceGen.Handlers;

/// <summary>
/// Handler for GetProductQuery - retrieves product details.
/// </summary>
public sealed class GetProductQueryHandler : IRequestHandler<GetProductQuery, Product>
{
    private readonly ILogger<GetProductQueryHandler> _logger;

    public GetProductQueryHandler(ILogger<GetProductQueryHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Product> HandleAsync(GetProductQuery request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching product with ID: {ProductId}", request.ProductId);

        // Simulate database lookup
        var product = new Product(
            Id: request.ProductId,
            Name: $"Product #{request.ProductId}",
            Price: 29.99m,
            StockQuantity: 150
        );

        return ValueTask.FromResult(product);
    }
}
