using Microsoft.EntityFrameworkCore;

namespace MediatorLite.RestApiBenchmarks.Data;

public enum DatasetProfile
{
    Medium = 0,
    Large = 1
}

public static class SeedData
{
    public static DatasetExpectedCounts GetExpectedCounts(DatasetProfile profile)
    {
        return profile == DatasetProfile.Large
            ? new DatasetExpectedCounts(2500, 4000, 18000, 25000)
            : new DatasetExpectedCounts(800, 1200, 6000, 10000);
    }

    public static async Task EnsureCreatedAndSeedAsync(
        AppDbContext dbContext,
        DatasetProfile profile,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureDeletedAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        var random = new Random(42);

        var expected = GetExpectedCounts(profile);
        var customerCount = expected.Customers;
        var productCount = expected.Products;
        var orderCount = expected.Orders;

        var customers = Enumerable.Range(1, customerCount)
            .Select(i => new Customer
            {
                Email = $"customer-{i}@bench.local",
                Name = $"Customer {i}",
                CreatedAtUtc = DateTime.UtcNow.AddDays(-random.Next(30, 1200))
            })
            .ToArray();

        await dbContext.Customers.AddRangeAsync(customers, cancellationToken);

        var products = Enumerable.Range(1, productCount)
            .Select(i => new Product
            {
                Sku = $"SKU-{i:D8}",
                Name = $"Product {i}",
                UnitPrice = decimal.Round((decimal)(5 + random.NextDouble() * 500), 2),
                AvailableStock = random.Next(50, 2000)
            })
            .ToArray();

        await dbContext.Products.AddRangeAsync(products, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var orders = new List<Order>(orderCount);
        var orderLines = new List<OrderLine>(orderCount * 3);

        for (var orderIndex = 0; orderIndex < orderCount; orderIndex++)
        {
            var customer = customers[random.Next(customers.Length)];
            var order = new Order
            {
                CustomerId = customer.Id,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-random.Next(5, 900000)),
                Status = random.NextDouble() < 0.9 ? "Completed" : "Pending",
            };

            var linesForOrder = random.Next(1, 5);
            var runningTotal = 0m;
            for (var lineIndex = 0; lineIndex < linesForOrder; lineIndex++)
            {
                var product = products[random.Next(products.Length)];
                var quantity = random.Next(1, 6);
                var lineTotal = product.UnitPrice * quantity;

                runningTotal += lineTotal;
                orderLines.Add(new OrderLine
                {
                    Order = order,
                    ProductId = product.Id,
                    Quantity = quantity,
                    UnitPrice = product.UnitPrice,
                    LineTotal = lineTotal
                });
            }

            order.TotalAmount = runningTotal;
            orders.Add(order);
        }

        await dbContext.Orders.AddRangeAsync(orders, cancellationToken);
        await dbContext.OrderLines.AddRangeAsync(orderLines, cancellationToken);

        var auditEntries = Enumerable.Range(1, expected.AuditEntries)
            .Select(i => new AuditEntry
            {
                Action = i % 2 == 0 ? "ORDER_CREATED" : "ORDER_STATUS_UPDATED",
                Payload = $"{{\"seq\":{i}}}",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-random.Next(10, 600000))
            })
            .ToArray();

        await dbContext.AuditEntries.AddRangeAsync(auditEntries, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed record DatasetExpectedCounts(int Customers, int Products, int Orders, int AuditEntries);
