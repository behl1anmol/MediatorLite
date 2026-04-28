using MediatorLite.RestApiBenchmarks.Application.Contracts;
using MediatorLite.RestApiBenchmarks.Data;
using Microsoft.EntityFrameworkCore;

namespace MediatorLite.RestApiBenchmarks.Application.Common;

public sealed class CreateOrderCommandValidator : IAppValidator<CreateOrderCommand>
{
    private readonly AppDbContext _dbContext;

    public CreateOrderCommandValidator(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<IReadOnlyList<string>> ValidateAsync(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        if (request.Lines.Count > 50)
        {
            errors.Add("A single order cannot contain more than 50 lines.");
        }

        var seenProductIds = new HashSet<int>(request.Lines.Count);
        var duplicatedProductIds = new HashSet<int>();

        foreach (var line in request.Lines)
        {
            if (!seenProductIds.Add(line.ProductId))
            {
                duplicatedProductIds.Add(line.ProductId);
            }
        }

        if (duplicatedProductIds.Count > 0)
        {
            errors.Add("Order lines cannot contain duplicated product IDs.");
        }

        var productIds = seenProductIds.ToArray();
        var products = await _dbContext.Products
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new { product.Id, product.AvailableStock })
            .ToArrayAsync(cancellationToken);

        if (products.Length != productIds.Length)
        {
            errors.Add("One or more products do not exist.");
        }

        var stockByProductId = products.ToDictionary(x => x.Id, x => x.AvailableStock);

        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0)
            {
                errors.Add($"Quantity must be positive for product {line.ProductId}.");
                continue;
            }

            if (stockByProductId.TryGetValue(line.ProductId, out var stock) && stock < line.Quantity)
            {
                errors.Add($"Insufficient stock for product {line.ProductId}.");
            }
        }

        return errors;
    }
}
