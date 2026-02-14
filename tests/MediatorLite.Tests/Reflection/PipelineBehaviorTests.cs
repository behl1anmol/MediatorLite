using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MediatorLite.Tests.Reflection;

public class PipelineBehaviorTests
{
    #region Test Types

    public record TestQuery(int Value) : IRequest<int>;

    [MediatorGeneration(Skip = true)]
    public class TestQueryHandler : IRequestHandler<TestQuery, int>
    {
        public ValueTask<int> HandleAsync(TestQuery request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(request.Value * 2);
        }
    }

    [MediatorGeneration(Skip = true)]
    public class AddOneBehavior : IPipelineBehavior<TestQuery, int>
    {
        public async ValueTask<int> HandleAsync(
            TestQuery request,
            RequestHandlerDelegate<int> next,
            CancellationToken cancellationToken = default)
        {
            var result = await next();
            return result + 1;
        }
    }

    [MediatorGeneration(Skip = true)]
    public class MultiplyByTwoBehavior : IPipelineBehavior<TestQuery, int>
    {
        public async ValueTask<int> HandleAsync(
            TestQuery request,
            RequestHandlerDelegate<int> next,
            CancellationToken cancellationToken = default)
        {
            var result = await next();
            return result * 2;
        }
    }

    [MediatorGeneration(Skip = true)]
    public class TrackingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public static List<string> Calls { get; } = [];

        public async ValueTask<TResponse> HandleAsync(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"Before: {typeof(TRequest).Name}");
            var result = await next();
            Calls.Add($"After: {typeof(TRequest).Name}");
            return result;
        }
    }

    #endregion

    [Fact]
    public async Task Behaviors_ExecuteInRegistrationOrder()
    {
        // Arrange
        // Handler: 5 * 2 = 10
        // Behaviors are resolved from DI in registration order via GetServices
        // First behavior AddOneBehavior wraps second, so:
        // AddOneBehavior -> MultiplyByTwoBehavior -> Handler(10) -> *2 = 20 -> +1 = 21
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<TestQuery, int>, TestQueryHandler>();
        services.AddTransient<IPipelineBehavior<TestQuery, int>, AddOneBehavior>();
        services.AddTransient<IPipelineBehavior<TestQuery, int>, MultiplyByTwoBehavior>();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new TestQuery(5));

        // Assert
        // AddOneBehavior wraps MultiplyByTwoBehavior which wraps Handler
        // Handler: 5 * 2 = 10
        // MultiplyByTwoBehavior: 10 * 2 = 20
        // AddOneBehavior: 20 + 1 = 21
        result.Should().Be(21);
    }

    [Fact]
    public async Task OpenGenericBehavior_IsAppliedToAllRequests()
    {
        // Arrange
        TrackingBehavior<TestQuery, int>.Calls.Clear();

        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<TestQuery, int>, TestQueryHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TrackingBehavior<,>));
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.SendAsync(new TestQuery(5));

        // Assert
        TrackingBehavior<TestQuery, int>.Calls.Should().Contain("Before: TestQuery");
        TrackingBehavior<TestQuery, int>.Calls.Should().Contain("After: TestQuery");
    }

    [Fact]
    public async Task Behavior_CanShortCircuit()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<TestQuery, int>, TestQueryHandler>();
        services.AddTransient<IPipelineBehavior<TestQuery, int>, ShortCircuitBehavior>();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new TestQuery(5));

        // Assert
        result.Should().Be(999); // Short-circuit value
    }

    [MediatorGeneration(Skip = true)]
    public class ShortCircuitBehavior : IPipelineBehavior<TestQuery, int>
    {
        public ValueTask<int> HandleAsync(
            TestQuery request,
            RequestHandlerDelegate<int> next,
            CancellationToken cancellationToken = default)
        {
            // Don't call next, return directly
            return ValueTask.FromResult(999);
        }
    }
}
