namespace MediatorLite.Sample.SourceGen.Requests;

/// <summary>
/// Query to get a product by ID.
/// </summary>
public sealed record GetProductQuery(int ProductId) : IRequest<Product>;

/// <summary>
/// Product model returned by the query.
/// </summary>
public sealed record Product(int Id, string Name, decimal Price, int StockQuantity);
