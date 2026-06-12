namespace MediatorLite.Sample.SourceGen.Requests;

/// <summary>
/// Command to create a new product.
/// Validation rules live in <see cref="Validators.CreateProductCommandValidator"/>
/// (FluentValidation) and are wired into the pipeline by the source generator.
/// </summary>
public sealed record CreateProductCommand : IRequest<int>
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public decimal Price { get; init; }

    public int InitialStock { get; init; }

    public required string Category { get; init; }
}
