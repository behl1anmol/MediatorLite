using FluentAssertions;
using MediatorLite.Generated;
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

    // Separate notification type for testing per-type settings without side effects from FailingHandler
    [NotificationOptions(
        ExecutionStrategy = NotificationExecutionStrategy.Parallel,
        ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate)]
    public record PerTypeSettingsNotification(string Message) : INotification;

    public class PerTypeSettingsHandler : INotificationHandler<PerTypeSettingsNotification>
    {
        public static bool WasCalled { get; private set; }

        public ValueTask HandleAsync(PerTypeSettingsNotification notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }

        public static void Reset() => WasCalled = false;
    }

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
            return ValueTask.FromException(new InvalidOperationException("Handler failed"));
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
        services.AddTransient<INotificationHandler<UserCreatedNotification>, FirstHandler>();
        services.AddTransient<INotificationHandler<UserCreatedNotification>, SecondHandler>();
        services.AddTransient<INotificationHandler<UserCreatedNotification>, OrderedFirstHandler>();
        services.AddMediatorLite();
        services.AddLogging();
        services.AddGeneratedHandlers();

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
        services.AddTransient<INotificationHandler<UserCreatedNotification>, FirstHandler>();
        services.AddTransient<INotificationHandler<UserCreatedNotification>, SecondHandler>();
        services.AddTransient<INotificationHandler<UserCreatedNotification>, OrderedFirstHandler>();
        services.AddMediatorLite(options =>
        {
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.Sequential;
        });
        services.AddLogging();
        services.AddGeneratedHandlers();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.PublishAsync(new UserCreatedNotification(1, "test@test.com"));

        // Assert - Order should be: FirstHandler(0), OrderedFirstHandler(1), SecondHandler(2)
        FirstHandler.CallOrder.Should().ContainInOrder(1, 0, 2);
    }

    [Fact]
    public async Task PublishAsync_WithNoHandlers_CompletesWithoutError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddLogging();
        services.AddGeneratedHandlers();

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
        services.AddGeneratedHandlers();

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
        PerTypeSettingsHandler.Reset();

        var services = new ServiceCollection();
        services.AddMediatorLite(options =>
        {
            // Global setting is Sequential
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.Sequential;
        });
        services.AddLogging();
        services.AddGeneratedHandlers();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - PerTypeSettingsNotification has [NotificationOptions] attribute overriding to Parallel
        await mediator.PublishAsync(new PerTypeSettingsNotification("test"));

        // Assert
        PerTypeSettingsHandler.WasCalled.Should().BeTrue();
    }

    #region StopOnFirst Tests

    [NotificationOptions(ExecutionStrategy = NotificationExecutionStrategy.StopOnFirst)]
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

    [NotificationOptions(
        ExecutionStrategy = NotificationExecutionStrategy.StopOnFirst,
        ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate)]
    public record FallbackNotification(string Message) : INotification;

    public class FallbackHandler1_Fails : INotificationHandler<FallbackNotification>
    {
        public static bool WasCalled { get; private set; }
        public static void Reset() => WasCalled = false;

        public ValueTask HandleAsync(FallbackNotification notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.FromException(new InvalidOperationException("Primary failed"));
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

    // Notification type for testing StopOnFirst with StopOnFirstError strategy
    [NotificationOptions(
        ExecutionStrategy = NotificationExecutionStrategy.StopOnFirst,
        ErrorStrategy = NotificationErrorStrategy.StopOnFirstError)]
    public record StopOnFirstErrorNotification(string Message) : INotification;

    public class StopOnFirstErrorHandler_Fails : INotificationHandler<StopOnFirstErrorNotification>
    {
        public static bool WasCalled { get; private set; }
        public static void Reset() => WasCalled = false;

        public ValueTask HandleAsync(StopOnFirstErrorNotification notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.FromException(new InvalidOperationException("Primary failed"));
        }
    }

    [NotificationHandlerOrder(1)]
    public class StopOnFirstErrorHandler_Succeeds : INotificationHandler<StopOnFirstErrorNotification>
    {
        public static bool WasCalled { get; private set; }
        public static void Reset() => WasCalled = false;

        public ValueTask HandleAsync(StopOnFirstErrorNotification notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    // Notification type for testing all handlers failing scenario
    [NotificationOptions(
        ExecutionStrategy = NotificationExecutionStrategy.StopOnFirst,
        ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate)]
    public record AllFailNotification(string Message) : INotification;

    public class AllFailHandler1 : INotificationHandler<AllFailNotification>
    {
        public ValueTask HandleAsync(AllFailNotification notification, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException(new InvalidOperationException("Handler1 failed"));
        }
    }

    [NotificationHandlerOrder(1)]
    public class AllFailHandler2 : INotificationHandler<AllFailNotification>
    {
        public ValueTask HandleAsync(AllFailNotification notification, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException(new InvalidOperationException("Handler2 failed"));
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
        services.AddGeneratedHandlers();

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
        services.AddGeneratedHandlers();

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
        StopOnFirstErrorHandler_Fails.Reset();
        StopOnFirstErrorHandler_Succeeds.Reset();

        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddLogging();
        services.AddGeneratedHandlers();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should throw immediately (StopOnFirstErrorNotification has StopOnFirstError attribute)
        Func<Task> act = async () => await mediator.PublishAsync(new StopOnFirstErrorNotification("test"));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Primary failed");

        // Handler_Succeeds (order 1) should NOT have been called because Handler_Fails (order 0) throws
        StopOnFirstErrorHandler_Fails.WasCalled.Should().BeTrue();
        StopOnFirstErrorHandler_Succeeds.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task PublishAsync_StopOnFirst_AllFail_ThrowsAggregateException()
    {
        // Arrange - AllFailNotification has only failing handlers
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddGeneratedHandlers();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should throw AggregateException with all failures
        Func<Task> act = async () => await mediator.PublishAsync(new AllFailNotification("test"));
        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().HaveCount(2);
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
        services.AddGeneratedHandlers();
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
