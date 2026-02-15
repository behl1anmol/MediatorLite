using MediatorLite.Sample.SourceGen.Requests;
using MediatorLite.Validation;
using Microsoft.Extensions.Logging;

namespace MediatorLite.Sample.SourceGen.Validators;

/// <summary>
/// Custom validator for CreateProductCommand that checks business rules.
/// This demonstrates validation beyond DataAnnotations.
/// </summary>
public sealed class CreateProductCommandValidator : IValidator<CreateProductCommand>
{
    private readonly ILogger<CreateProductCommandValidator> _logger;

    // Simulated list of restricted product names
    private static readonly HashSet<string> RestrictedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Test", "Debug", "Admin", "System"
    };

    // Simulated list of allowed categories
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Electronics", "Clothing", "Books", "Home & Garden", "Sports", "Toys"
    };

    public CreateProductCommandValidator(ILogger<CreateProductCommandValidator> logger)
    {
        _logger = logger;
    }

    public ValueTask<ValidationResult> ValidateAsync(
        CreateProductCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Running custom business validation for product: {Name}", request.Name);

        var errors = new List<ValidationError>();

        // Business rule 1: Check for restricted product names
        if (RestrictedNames.Contains(request.Name))
        {
            errors.Add(new ValidationError(
                nameof(request.Name),
                $"Product name '{request.Name}' is restricted and cannot be used",
                request.Name));
        }

        // Business rule 2: Validate category against allowed list
        if (!AllowedCategories.Contains(request.Category))
        {
            errors.Add(new ValidationError(
                nameof(request.Category),
                $"Category '{request.Category}' is not valid. Allowed categories: {string.Join(", ", AllowedCategories)}",
                request.Category));
        }

        // Business rule 3: High-value products need approval (just a warning in logs)
        if (request.Price > 1000m)
        {
            _logger.LogWarning(
                "⚠️ High-value product detected: {Name} (${Price}). May require manager approval.",
                request.Name,
                request.Price);
        }

        // Business rule 4: Initial stock should be reasonable for the price
        if (request.Price > 500m && request.InitialStock > 1000)
        {
            errors.Add(new ValidationError(
                nameof(request.InitialStock),
                "High-value products (>$500) cannot have initial stock exceeding 1000 units",
                request.InitialStock));
        }

        if (errors.Count > 0)
        {
            _logger.LogWarning("Validation failed with {ErrorCount} error(s)", errors.Count);
            return ValueTask.FromResult(ValidationResult.Failure(errors));
        }

        _logger.LogDebug("✅ Custom validation passed");
        return ValueTask.FromResult(ValidationResult.Success);
    }
}
