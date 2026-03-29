using MediatorLite.RestApiBenchmarks.Application.Contracts;
using MediatorLite.RestApiBenchmarks.Data;
using Microsoft.EntityFrameworkCore;

namespace MediatorLite.RestApiBenchmarks.Application.Common;

public sealed class OrderApplicationService
{
    private readonly AppDbContext _dbContext;

    public OrderApplicationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CreateOrderResult> CreateOrderAsync(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var customer = await _dbContext.Customers
            .FirstOrDefaultAsync(x => x.Id == request.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException($"Customer {request.CustomerId} was not found.");

        var productIds = request.Lines.Select(line => line.ProductId).Distinct().ToArray();
        var products = await _dbContext.Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = new Order
        {
            CustomerId = customer.Id,
            CreatedAtUtc = DateTime.UtcNow,
            Status = "Pending"
        };

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var total = 0m;
        foreach (var line in request.Lines)
        {
            var product = products[line.ProductId];
            product.AvailableStock -= line.Quantity;

            var lineTotal = product.UnitPrice * line.Quantity;
            total += lineTotal;

            _dbContext.OrderLines.Add(new OrderLine
            {
                OrderId = order.Id,
                ProductId = product.Id,
                Quantity = line.Quantity,
                UnitPrice = product.UnitPrice,
                LineTotal = lineTotal
            });
        }

        order.TotalAmount = total;
        order.Status = "Created";

        _dbContext.AuditEntries.Add(new AuditEntry
        {
            Action = "ORDER_CREATED",
            Payload = $"{{\"orderId\":{order.Id},\"total\":{total}}}",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CreateOrderResult(order.Id, total, order.Status);
    }

    public async Task<OrderDetailsDto> GetOrderDetailsAsync(GetOrderDetailsQuery request, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.Lines)
            .ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == request.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {request.OrderId} was not found.");

        var lines = order.Lines
            .Select(line => new OrderLineDto(line.ProductId, line.Product.Name, line.Quantity, line.UnitPrice, line.LineTotal))
            .ToArray();

        return new OrderDetailsDto(order.Id, order.CustomerId, order.Customer.Name, order.Status, order.TotalAmount, order.CreatedAtUtc, lines);
    }

    public async Task<IReadOnlyList<OrderListItemDto>> SearchOrdersAsync(SearchOrdersQuery request, CancellationToken cancellationToken)
    {
        var skip = (request.Page - 1) * request.PageSize;

        return await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.CustomerId == request.CustomerId)
            .OrderByDescending(order => order.CreatedAtUtc)
            .Skip(skip)
            .Take(request.PageSize)
            .Select(order => new OrderListItemDto(order.Id, order.CustomerId, order.Status, order.TotalAmount, order.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<SalesReportDto> GetSalesReportAsync(GetSalesReportQuery request, CancellationToken cancellationToken)
    {
        var windowStart = DateTime.UtcNow.AddDays(-request.Days);

        var projection = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.CreatedAtUtc >= windowStart && order.Status != "Cancelled")
            .GroupBy(_ => 1)
            .Select(group => new
            {
                OrderCount = group.Count(),
                GrossRevenue = group.Sum(order => order.TotalAmount)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (projection is null)
        {
            return new SalesReportDto(0, 0m, 0m);
        }

        var average = projection.OrderCount == 0 ? 0m : projection.GrossRevenue / projection.OrderCount;
        return new SalesReportDto(projection.OrderCount, projection.GrossRevenue, average);
    }

    public async Task<CancelOrderResult> CancelOrderAsync(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == request.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {request.OrderId} was not found.");

        if (order.Status == "Cancelled")
        {
            throw new InvalidOperationException($"Order {request.OrderId} is already cancelled.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var productIds = order.Lines.Select(x => x.ProductId).ToArray();
        var products = await _dbContext.Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        foreach (var line in order.Lines)
        {
            products[line.ProductId].AvailableStock += line.Quantity;
        }

        order.Status = "Cancelled";
        var cancelledAt = DateTime.UtcNow;

        _dbContext.AuditEntries.Add(new AuditEntry
        {
            Action = "ORDER_CANCELLED",
            Payload = $"{{\"orderId\":{order.Id},\"reason\":\"{request.Reason}\"}}",
            CreatedAtUtc = cancelledAt
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CancelOrderResult(order.Id, order.Status, cancelledAt);
    }

    public async Task<UpdateOrderStatusResult> UpdateOrderStatusAsync(UpdateOrderStatusCommand request, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(x => x.Id == request.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {request.OrderId} was not found.");

        if (!string.Equals(order.Status, request.ExpectedCurrentStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new OrderConcurrencyException(
                $"Concurrency conflict for order {order.Id}. Expected '{request.ExpectedCurrentStatus}', actual '{order.Status}'.");
        }

        var previousStatus = order.Status;
        order.Status = request.NewStatus;
        var updatedAt = DateTime.UtcNow;

        _dbContext.AuditEntries.Add(new AuditEntry
        {
            Action = "ORDER_STATUS_UPDATED",
            Payload = $"{{\"orderId\":{order.Id},\"from\":\"{previousStatus}\",\"to\":\"{request.NewStatus}\"}}",
            CreatedAtUtc = updatedAt
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new UpdateOrderStatusResult(order.Id, previousStatus, request.NewStatus, updatedAt);
    }

    public async Task<CustomerOrderSummaryDto> GetCustomerSummaryAsync(GetCustomerOrderSummaryQuery request, CancellationToken cancellationToken)
    {
        var windowStart = DateTime.UtcNow.AddDays(-request.Days);

        var query = _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.CustomerId == request.CustomerId && order.CreatedAtUtc >= windowStart);

        var grouped = await query
            .GroupBy(_ => 1)
            .Select(group => new
            {
                OrderCount = group.Count(),
                TotalSpent = group.Sum(order => order.TotalAmount),
                LastOrderAtUtc = group.Max(order => order.CreatedAtUtc),
                Pending = group.Count(order => order.Status == "Pending"),
                Cancelled = group.Count(order => order.Status == "Cancelled")
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (grouped is null)
        {
            return new CustomerOrderSummaryDto(request.CustomerId, 0, 0m, null, 0, 0);
        }

        return new CustomerOrderSummaryDto(
            request.CustomerId,
            grouped.OrderCount,
            grouped.TotalSpent,
            grouped.LastOrderAtUtc,
            grouped.Pending,
            grouped.Cancelled);
    }

    public async Task WriteAuditAsync(string action, string payload, CancellationToken cancellationToken)
    {
        _dbContext.AuditEntries.Add(new AuditEntry
        {
            Action = action,
            Payload = payload,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
