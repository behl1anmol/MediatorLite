namespace MediatorLite.Sample.SourceGen.Requests;

/// <summary>
/// Command to update product stock (void command - no return value).
/// </summary>
public sealed record UpdateStockCommand(int ProductId, int QuantityChange) : IRequest;
