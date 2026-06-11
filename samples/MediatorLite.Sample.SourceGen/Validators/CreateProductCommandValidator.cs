using FluentValidation;
using MediatorLite.Sample.SourceGen.Requests;

namespace MediatorLite.Sample.SourceGen.Validators;

/// <summary>
/// FluentValidation validator for <see cref="CreateProductCommand"/>. Combines
/// property-level constraints with business rules. The MediatorLite source generator
/// discovers this validator at compile time and runs it as the outermost pipeline
/// behavior — no assembly scanning, no manual registration.
/// </summary>
public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    // Simulated list of restricted product names.
    private static readonly HashSet<string> RestrictedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Test", "Debug", "Admin", "System"
    };

    // Simulated list of allowed categories.
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Electronics", "Clothing", "Books", "Home & Garden", "Sports", "Toys"
    };

    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required")
            .Length(3, 100).WithMessage("Product name must be between 3 and 100 characters")
            .Must(name => !RestrictedNames.Contains(name))
                .WithMessage(x => $"Product name '{x.Name}' is restricted and cannot be used");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required")
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters");

        RuleFor(x => x.Price)
            .InclusiveBetween(0.01m, 10000.00m)
                .WithMessage("Price must be between $0.01 and $10,000.00");

        RuleFor(x => x.InitialStock)
            .InclusiveBetween(0, 10000)
                .WithMessage("Initial stock must be between 0 and 10,000");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required")
            .MaximumLength(50).WithMessage("Category cannot exceed 50 characters")
            .Must(category => AllowedCategories.Contains(category))
                .WithMessage(x => $"Category '{x.Category}' is not valid. Allowed categories: {string.Join(", ", AllowedCategories)}");

        // Cross-field business rule: high-value products cannot carry excessive stock.
        RuleFor(x => x.InitialStock)
            .LessThanOrEqualTo(1000)
            .When(x => x.Price > 500m)
            .WithMessage("High-value products (>$500) cannot have initial stock exceeding 1000 units");
    }
}
