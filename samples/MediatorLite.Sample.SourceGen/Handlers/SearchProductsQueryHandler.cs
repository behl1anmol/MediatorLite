using Microsoft.Extensions.Logging;
using MediatorLite.Sample.SourceGen.Requests;

namespace MediatorLite.Sample.SourceGen.Handlers;

/// <summary>
/// Handler for SearchProductsQuery - searches for products by term.
/// </summary>
public sealed class SearchProductsQueryHandler : IRequestHandler<SearchProductsQuery, IReadOnlyList<Product>>
{
    private readonly ILogger<SearchProductsQueryHandler> _logger;

    public SearchProductsQueryHandler(ILogger<SearchProductsQueryHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<IReadOnlyList<Product>> HandleAsync(SearchProductsQuery request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Searching products with term: '{SearchTerm}', max results: {MaxResults}",
            request.SearchTerm, request.MaxResults);

        // Simulate search results
        var products = Enumerable.Range(1, Math.Min(3, request.MaxResults))
            .Select(i => new Product(
                Id: i,
                Name: $"{request.SearchTerm} Product {i}",
                Price: 19.99m + i,
                StockQuantity: 100 * i
            ))
            .ToList();

        return ValueTask.FromResult<IReadOnlyList<Product>>(products);
    }
}
