using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MediatorLite.Tests.Reflection;

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

    #region StopOnFirst Tests

    public record StopOnFirstNotification(string Message) : INotification;

    public class StopOnFirstHandler1 : INotificationHandler<StopOnFirstNotification>
    {
        public static bool WasCalled { get; private set; }
        public static void Reset() => WasCalled = false;

        public ValueTask HandleAsync(StopOnFirstNotification notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    [NotificationHandlerOrder(1)]
    public class StopOnFirstHandler2 : INotificationHandler<StopOnFirstNotification>
    {
        public static bool WasCalled { get; private set; }
        public static void Reset() => WasCalled = false;

        public ValueTask HandleAsync(StopOnFirstNotification notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    public record FallbackNotification(string Message) : INotification;

    public class FallbackHandler1_Fails : INotificationHandler<FallbackNotification>
    {
        public static bool WasCalled { get; private set; }
        public static void Reset() => WasCalled = false;

        public ValueTask HandleAsync(FallbackNotification notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            throw new InvalidOperationException("Primary failed");
        }
    }

    [NotificationHandlerOrder(1)]
    public class FallbackHandler2_Succeeds : INotificationHandler<FallbackNotification>
    {
        public static bool WasCalled { get; private set; }
        public static void Reset() => WasCalled = false;

        public ValueTask HandleAsync(FallbackNotification notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    [NotificationHandlerOrder(2)]
    public class FallbackHandler3_NotReached : INotificationHandler<FallbackNotification>
    {
        public static bool WasCalled { get; private set; }
        public static void Reset() => WasCalled = false;

        public ValueTask HandleAsync(FallbackNotification notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_StopOnFirst_StopsAfterFirstSuccess()
    {
        // Arrange
        StopOnFirstHandler1.Reset();
        StopOnFirstHandler2.Reset();

        var services = new ServiceCollection();
        services.AddTransient<INotificationHandler<StopOnFirstNotification>, StopOnFirstHandler1>();
        services.AddTransient<INotificationHandler<StopOnFirstNotification>, StopOnFirstHandler2>();
        services.AddMediatorLite(options =>
        {
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.StopOnFirst;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.PublishAsync(new StopOnFirstNotification("test"));

        // Assert - Only first handler (by order) should run
        StopOnFirstHandler1.WasCalled.Should().BeTrue();
        StopOnFirstHandler2.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task PublishAsync_StopOnFirst_WithContinueAndAggregate_TriesNextOnFailure()
    {
        // Arrange - Fallback pattern: Handler1 fails, Handler2 succeeds, Handler3 should not run
        FallbackHandler1_Fails.Reset();
        FallbackHandler2_Succeeds.Reset();
        FallbackHandler3_NotReached.Reset();

        var services = new ServiceCollection();
        services.AddTransient<INotificationHandler<FallbackNotification>, FallbackHandler1_Fails>();
        services.AddTransient<INotificationHandler<FallbackNotification>, FallbackHandler2_Succeeds>();
        services.AddTransient<INotificationHandler<FallbackNotification>, FallbackHandler3_NotReached>();
        services.AddMediatorLite(options =>
        {
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.StopOnFirst;
            options.NotificationErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Should NOT throw because Handler2 succeeds
        await mediator.PublishAsync(new FallbackNotification("test"));

        // Assert
        FallbackHandler1_Fails.WasCalled.Should().BeTrue("Handler1 is tried first");
        FallbackHandler2_Succeeds.WasCalled.Should().BeTrue("Handler2 is tried after Handler1 fails");
        FallbackHandler3_NotReached.WasCalled.Should().BeFalse("Handler3 should not run after Handler2 succeeds");
    }

    [Fact]
    public async Task PublishAsync_StopOnFirst_WithStopOnFirstError_ThrowsImmediately()
    {
        // Arrange
        FallbackHandler1_Fails.Reset();
        FallbackHandler2_Succeeds.Reset();

        var services = new ServiceCollection();
        services.AddTransient<INotificationHandler<FallbackNotification>, FallbackHandler1_Fails>();
        services.AddTransient<INotificationHandler<FallbackNotification>, FallbackHandler2_Succeeds>();
        services.AddMediatorLite(options =>
        {
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.StopOnFirst;
            options.NotificationErrorStrategy = NotificationErrorStrategy.StopOnFirstError;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should throw immediately
        Func<Task> act = async () => await mediator.PublishAsync(new FallbackNotification("test"));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Primary failed");

        // Handler2 should NOT have been called
        FallbackHandler2_Succeeds.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task PublishAsync_StopOnFirst_AllFail_ThrowsAggregateException()
    {
        // Arrange - All handlers fail
        var services = new ServiceCollection();
        services.AddTransient<INotificationHandler<FallbackNotification>, FallbackHandler1_Fails>();
        services.AddTransient<INotificationHandler<FallbackNotification>>(_ => 
            new ThrowingFallbackHandler("Handler2 also failed"));
        services.AddMediatorLite(options =>
        {
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.StopOnFirst;
            options.NotificationErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should throw AggregateException with all failures
        Func<Task> act = async () => await mediator.PublishAsync(new FallbackNotification("test"));
        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().HaveCount(2);
    }

    public class ThrowingFallbackHandler(string message) : INotificationHandler<FallbackNotification>
    {
        public ValueTask HandleAsync(FallbackNotification notification, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(message);
        }
    }

    #endregion

    #region Parallel Tests - Error Strategy Ignored

    [Fact]
    public async Task PublishAsync_Parallel_AlwaysAggregates_RegardlessOfErrorStrategy()
    {
        // Arrange - Set StopOnFirstError but parallel should ignore it
        SuccessHandler.Reset();

        var services = new ServiceCollection();
        services.AddTransient<INotificationHandler<ParallelNotification>, FailingHandler>();
        services.AddTransient<INotificationHandler<ParallelNotification>, SuccessHandler>();
        services.AddMediatorLite(options =>
        {
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.Parallel;
            options.NotificationErrorStrategy = NotificationErrorStrategy.StopOnFirstError; // Should be ignored!
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should throw AggregateException (not individual exception)
        Func<Task> act = async () => await mediator.PublishAsync(new ParallelNotification("test"));
        await act.Should().ThrowAsync<AggregateException>();

        // Success handler should have been called because parallel always runs all handlers
        SuccessHandler.WasCalled.Should().BeTrue("Parallel execution runs all handlers regardless of failures");
    }

    #endregion
}
