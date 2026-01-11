namespace MediatorLite.Sample.SourceGen.Requests;

/// <summary>
/// Query to search for products.
/// </summary>
public sealed record SearchProductsQuery(string SearchTerm, int MaxResults = 10) : IRequest<IReadOnlyList<Product>>;
