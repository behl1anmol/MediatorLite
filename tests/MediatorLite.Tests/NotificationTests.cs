using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MediatorLite.Tests;

public class NotificationTests
{
    #region Test Types

    public record UserCreatedNotification(int UserId, string Email) : INotification;

    [NotificationOptions(
        ExecutionStrategy = NotificationExecutionStrategy.Parallel,
        ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate)]
    public record ParallelNotification(string Message) : INotification;

    public class FirstHandler : INotificationHandler<UserCreatedNotification>
    {
        public static List<int> CallOrder { get; } = [];
        public static int CallCount { get; private set; }

        public ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken cancellationToken = default)
        {
            CallCount++;
            CallOrder.Add(1);
            return ValueTask.CompletedTask;
        }

        public static void Reset()
        {
            CallCount = 0;
            CallOrder.Clear();
        }
    }

    [NotificationHandlerOrder(2)]
    public class SecondHandler : INotificationHandler<UserCreatedNotification>
    {
        public static int CallCount { get; private set; }

        public ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken cancellationToken = default)
        {
            CallCount++;
            FirstHandler.CallOrder.Add(2);
            return ValueTask.CompletedTask;
        }

        public static void Reset() => CallCount = 0;
    }

    [NotificationHandlerOrder(1)]
    public class OrderedFirstHandler : INotificationHandler<UserCreatedNotification>
    {
        public static int CallCount { get; private set; }

        public ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken cancellationToken = default)
        {
            CallCount++;
            FirstHandler.CallOrder.Add(0);
            return ValueTask.CompletedTask;
        }

        public static void Reset() => CallCount = 0;
    }

    public class FailingHandler : INotificationHandler<ParallelNotification>
    {
        public ValueTask HandleAsync(ParallelNotification notification, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Handler failed");
        }
    }

    public class SuccessHandler : INotificationHandler<ParallelNotification>
    {
        public static bool WasCalled { get; private set; }

        public ValueTask HandleAsync(ParallelNotification notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }

        public static void Reset() => WasCalled = false;
    }

    #endregion

    [Fact]
    public async Task PublishAsync_InvokesAllHandlers()
    {
        // Arrange
        FirstHandler.Reset();
        SecondHandler.Reset();
        OrderedFirstHandler.Reset();

        var services = new ServiceCollection();
        services.AddMediatorLite(options =>
        {
            options.RegisterHandlersFromAssemblyContaining<NotificationTests>();
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.PublishAsync(new UserCreatedNotification(1, "test@test.com"));

        // Assert
        FirstHandler.CallCount.Should().Be(1);
        SecondHandler.CallCount.Should().Be(1);
        OrderedFirstHandler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_RespectsHandlerOrder()
    {
        // Arrange
        FirstHandler.Reset();
        SecondHandler.Reset();
        OrderedFirstHandler.Reset();

        var services = new ServiceCollection();
        services.AddMediatorLite(options =>
        {
            options.RegisterHandlersFromAssemblyContaining<NotificationTests>();
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.Sequential;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.PublishAsync(new UserCreatedNotification(1, "test@test.com"));

        // Assert - Order should be: OrderedFirstHandler (1), FirstHandler (default 0), SecondHandler (2)
        // Note: FirstHandler has no attribute so order=0, OrderedFirstHandler has order=1, SecondHandler has order=2
        // So actual order: FirstHandler(0), OrderedFirstHandler(1), SecondHandler(2)
        FirstHandler.CallOrder.Should().ContainInOrder(1, 0, 2);
    }

    [Fact]
    public async Task PublishAsync_WithNoHandlers_CompletesWithoutError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        Func<Task> act = async () => await mediator.PublishAsync(new UserCreatedNotification(1, "test@test.com"));

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_WithContinueOnError_CollectsAllExceptions()
    {
        // Arrange
        SuccessHandler.Reset();

        var services = new ServiceCollection();
        // Register FailingHandler first to prove that SuccessHandler still runs
        services.AddTransient<INotificationHandler<ParallelNotification>, FailingHandler>();
        services.AddTransient<INotificationHandler<ParallelNotification>, SuccessHandler>();
        services.AddMediatorLite(options =>
        {
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.Parallel;
            options.NotificationErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should throw AggregateException with the handler's error
        Func<Task> act = async () => await mediator.PublishAsync(new ParallelNotification("test"));
        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().ContainSingle()
            .Which.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("Handler failed");

        // SuccessHandler should have been called since we use ContinueAndAggregate
        SuccessHandler.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_UsesPerNotificationTypeSettings()
    {
        // Arrange
        SuccessHandler.Reset();

        var services = new ServiceCollection();
        services.AddTransient<INotificationHandler<ParallelNotification>, SuccessHandler>();
        services.AddMediatorLite(options =>
        {
            // Global setting is Sequential
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.Sequential;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - ParallelNotification has [NotificationOptions] attribute overriding to Parallel
        await mediator.PublishAsync(new ParallelNotification("test"));

        // Assert
        SuccessHandler.WasCalled.Should().BeTrue();
    }
}
