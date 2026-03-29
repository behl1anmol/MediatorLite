using MediatorLite.RestApiBenchmarks.Application.Common;
using MediatorLite.RestApiBenchmarks.Application.Contracts;
using MediatorLite.RestApiBenchmarks.Benchmarking;
using MediatorLite.RestApiBenchmarks.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ML = global::MediatorLite;
using MR = global::MediatR;

namespace MediatorLite.RestApiBenchmarks.Hosting;

public sealed class BenchmarkParityViolationException : Exception
{
    public BenchmarkParityViolationException(string message)
        : base(message)
    {
    }
}

public static class BenchmarkParityGuard
{
    public static async Task ValidateAsync(
        IServiceProvider serviceProvider,
        MediatorImplementation mediatorImplementation,
        DatasetProfile datasetProfile,
        CancellationToken cancellationToken)
    {
        ValidateMediatorRegistration(serviceProvider, mediatorImplementation);
        ValidatePipelineParity(serviceProvider, mediatorImplementation);
        ValidateNotificationParity(serviceProvider, mediatorImplementation);
        ValidateValidationParity(serviceProvider);
        await ValidateDatasetParityAsync(serviceProvider, datasetProfile, cancellationToken);
    }

    private static void ValidateMediatorRegistration(IServiceProvider serviceProvider, MediatorImplementation mediatorImplementation)
    {
        var hasMediatorLite = serviceProvider.GetService<ML.IMediator>() is not null;
        var hasMediatR = serviceProvider.GetService<MR.IMediator>() is not null;

        if (mediatorImplementation == MediatorImplementation.MediatorLite && (!hasMediatorLite || hasMediatR))
        {
            throw new BenchmarkParityViolationException("Expected only MediatorLite mediator registration for MediatorLite mode.");
        }

        if (mediatorImplementation == MediatorImplementation.MediatR && (!hasMediatR || hasMediatorLite))
        {
            throw new BenchmarkParityViolationException("Expected only MediatR mediator registration for MediatR mode.");
        }
    }

    private static void ValidatePipelineParity(IServiceProvider serviceProvider, MediatorImplementation mediatorImplementation)
    {
        const int expectedBehaviorCount = 3;

        if (mediatorImplementation == MediatorImplementation.MediatorLite)
        {
            var behaviorCount = serviceProvider
                .GetServices<ML.IPipelineBehavior<CreateOrderCommand, CreateOrderResult>>()
                .Count();

            if (behaviorCount != expectedBehaviorCount)
            {
                throw new BenchmarkParityViolationException($"MediatorLite behavior count mismatch. Expected {expectedBehaviorCount}, got {behaviorCount}.");
            }

            return;
        }

        var mediatRBehaviorCount = serviceProvider
            .GetServices<MR.IPipelineBehavior<CreateOrderCommand, CreateOrderResult>>()
            .Count();

        if (mediatRBehaviorCount != expectedBehaviorCount)
        {
            throw new BenchmarkParityViolationException($"MediatR behavior count mismatch. Expected {expectedBehaviorCount}, got {mediatRBehaviorCount}.");
        }
    }

    private static void ValidateNotificationParity(IServiceProvider serviceProvider, MediatorImplementation mediatorImplementation)
    {
        const int expectedNotificationHandlers = 3;

        if (mediatorImplementation == MediatorImplementation.MediatorLite)
        {
            var handlers = serviceProvider.GetServices<ML.INotificationHandler<OrderCreatedNotification>>().Count();
            if (handlers != expectedNotificationHandlers)
            {
                throw new BenchmarkParityViolationException($"MediatorLite notification handler count mismatch. Expected {expectedNotificationHandlers}, got {handlers}.");
            }

            return;
        }

        var mediatRHandlers = serviceProvider.GetServices<MR.INotificationHandler<OrderCreatedNotification>>().Count();
        if (mediatRHandlers != expectedNotificationHandlers)
        {
            throw new BenchmarkParityViolationException($"MediatR notification handler count mismatch. Expected {expectedNotificationHandlers}, got {mediatRHandlers}.");
        }
    }

    private static void ValidateValidationParity(IServiceProvider serviceProvider)
    {
        const int expectedValidatorCount = 1;
        var validatorCount = serviceProvider.GetServices<IAppValidator<CreateOrderCommand>>().Count();

        if (validatorCount != expectedValidatorCount)
        {
            throw new BenchmarkParityViolationException($"Validator count mismatch. Expected {expectedValidatorCount}, got {validatorCount}.");
        }
    }

    private static async Task ValidateDatasetParityAsync(
        IServiceProvider serviceProvider,
        DatasetProfile datasetProfile,
        CancellationToken cancellationToken)
    {
        var expected = SeedData.GetExpectedCounts(datasetProfile);

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var customerCount = await dbContext.Customers.CountAsync(cancellationToken);
        var productCount = await dbContext.Products.CountAsync(cancellationToken);
        var orderCount = await dbContext.Orders.CountAsync(cancellationToken);
        var auditCount = await dbContext.AuditEntries.CountAsync(cancellationToken);

        if (customerCount != expected.Customers)
        {
            throw new BenchmarkParityViolationException($"Customer seed count mismatch. Expected {expected.Customers}, got {customerCount}.");
        }

        if (productCount != expected.Products)
        {
            throw new BenchmarkParityViolationException($"Product seed count mismatch. Expected {expected.Products}, got {productCount}.");
        }

        if (orderCount != expected.Orders)
        {
            throw new BenchmarkParityViolationException($"Order seed count mismatch. Expected {expected.Orders}, got {orderCount}.");
        }

        if (auditCount != expected.AuditEntries)
        {
            throw new BenchmarkParityViolationException($"Audit seed count mismatch. Expected {expected.AuditEntries}, got {auditCount}.");
        }
    }
}
