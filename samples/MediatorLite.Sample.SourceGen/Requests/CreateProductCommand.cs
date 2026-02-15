using System.ComponentModel.DataAnnotations;

namespace MediatorLite.Sample.SourceGen.Requests;

/// <summary>
/// Command to create a new product with validation.
/// Demonstrates DataAnnotations validation and custom validators.
/// </summary>
public sealed record CreateProductCommand : IRequest<int>
{
    [Required(ErrorMessage = "Product name is required")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Product name must be between 3 and 100 characters")]
    public required string Name { get; init; }

    [Required(ErrorMessage = "Description is required")]
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public required string Description { get; init; }

    [Range(0.01, 10000.00, ErrorMessage = "Price must be between $0.01 and $10,000.00")]
    public decimal Price { get; init; }

    [Range(0, 10000, ErrorMessage = "Initial stock must be between 0 and 10,000")]
    public int InitialStock { get; init; }

    [Required(ErrorMessage = "Category is required")]
    [StringLength(50, ErrorMessage = "Category cannot exceed 50 characters")]
    public required string Category { get; init; }
}
