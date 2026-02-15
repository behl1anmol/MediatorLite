using FluentAssertions;
using MediatorLite.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediatorLite.Tests.SourceGeneration;

/// <summary>
/// Tests for notification functionality when using source-generated handler registration.
/// These tests verify that notification handlers are discovered at compile-time,
/// handler ordering is respected, and execution/error strategies work correctly.
/// </summary>
public class NotificationTests
{
    [Fact]
    public async Task PublishAsync_InvokesAllHandlers_WithSourceGen()
    {
        // Arrange
        UserCreatedEventHandler1.Reset();
        UserCreatedEventHandler2.Reset();
        UserCreatedEventHandler3.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.PublishAsync(new UserCreatedEvent(1, "test@test.com"));

        // Assert
        UserCreatedEventHandler1.CallCount.Should().Be(1);
        UserCreatedEventHandler2.CallCount.Should().Be(1);
        UserCreatedEventHandler3.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_RespectsHandlerOrder_WithSourceGen()
    {
        // Arrange
        UserCreatedEventHandler1.Reset();
        UserCreatedEventHandler2.Reset();
        UserCreatedEventHandler3.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite(options =>
        {
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.Sequential;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.PublishAsync(new UserCreatedEvent(1, "test@test.com"));

        // Assert - Order should be: Handler1 (default 0), Handler2 (order 1), Handler3 (order 2)
        UserCreatedEventHandler1.CallOrder.Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public async Task PublishAsync_UsesSourceGenHandlerOrder()
    {
        // This test verifies that source-generated TryGetHandlerOrder is used

        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var sourceGenMediator = provider.GetRequiredService<ISourceGeneratedMediator>();

        // Act - Check if source-gen knows the handler order
        var handler2Order = sourceGenMediator.TryGetHandlerOrder(typeof(UserCreatedEventHandler2));
        var handler3Order = sourceGenMediator.TryGetHandlerOrder(typeof(UserCreatedEventHandler3));

        // Assert
        handler2Order.Should().Be(1);
        handler3Order.Should().Be(2);
    }

    [Fact]
    public async Task PublishAsync_WithNoHandlers_CompletesWithoutError()
    {
        // Arrange - Create a notification type that has no handlers registered
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.PublishAsync(new UserCreatedEvent(1, "test@test.com"));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_WithParallelStrategy_AllHandlersRun()
    {
        // Arrange
        ParallelEventSuccessHandler.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - ParallelEvent has ContinueAndAggregate, so success handler should run
        // even though failing handler throws
        Func<Task> act = async () => await mediator.PublishAsync(new ParallelEvent("test"));
        await act.Should().ThrowAsync<AggregateException>();

        ParallelEventSuccessHandler.WasCalled.Should().BeTrue(
            "Success handler should run even when another handler fails with ContinueAndAggregate strategy");
    }

    [Fact]
    public async Task PublishAsync_UsesPerNotificationSettings_FromAttribute()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var sourceGenMediator = provider.GetRequiredService<ISourceGeneratedMediator>();

        // Act - Check if source-gen knows the notification options
        var parallelOptions = sourceGenMediator.TryGetNotificationOptions(typeof(ParallelEvent));

        // Assert
        parallelOptions.Should().NotBeNull();
        parallelOptions!.Value.ExecutionStrategy.Should().Be(NotificationExecutionStrategy.Parallel);
        parallelOptions!.Value.ErrorStrategy.Should().Be(NotificationErrorStrategy.ContinueAndAggregate);
    }

    [Fact]
    public async Task PublishAsync_WithStopOnFirstStrategy_StopsAfterFirst()
    {
        // Arrange
        StopOnFirstEventHandler1.Reset();
        StopOnFirstEventHandler2.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.PublishAsync(new StopOnFirstEvent("test"));

        // Assert - Only the first handler (by order) should be called
        // Handler1 has order 0 (default), Handler2 has order 1
        // So Handler1 runs first and StopOnFirst stops there
        StopOnFirstEventHandler1.WasCalled.Should().BeTrue();
        StopOnFirstEventHandler2.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task PublishAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        UserCreatedEventHandler1.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        Func<Task> act = async () => await mediator.PublishAsync(new UserCreatedEvent(1, "test@test.com"), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PublishAsync_GlobalStrategy_CanBeOverriddenByAttribute()
    {
        // Arrange - Set global to Sequential, but ParallelEvent has Parallel attribute
        ParallelEventSuccessHandler.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite(options =>
        {
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.Sequential;
            options.NotificationErrorStrategy = NotificationErrorStrategy.StopOnFirstError;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should use attribute settings (Parallel, ContinueAndAggregate)
        // which means AggregateException instead of just the first exception
        Func<Task> act = async () => await mediator.PublishAsync(new ParallelEvent("test"));
        await act.Should().ThrowAsync<AggregateException>();

        // Success handler should have been called due to ContinueAndAggregate
        ParallelEventSuccessHandler.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_WithTracing_DoesNotThrow()
    {
        // Arrange
        UserCreatedEventHandler1.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite(options =>
        {
            options.EnableTracing = true;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.PublishAsync(new UserCreatedEvent(1, "test@test.com"));
        await act.Should().NotThrowAsync();

        UserCreatedEventHandler1.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_WithLogging_DoesNotThrow()
    {
        // Arrange
        UserCreatedEventHandler1.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite(options =>
        {
            options.EnableBuiltInLogging = true;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.PublishAsync(new UserCreatedEvent(1, "test@test.com"));
        await act.Should().NotThrowAsync();

        UserCreatedEventHandler1.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_StopOnFirst_WithContinueAndAggregate_FallsBackToNextHandler()
    {
        // Arrange - StopOnFirstFallbackEvent is configured with StopOnFirst + ContinueAndAggregate
        // Handler1 fails, Handler2 succeeds, Handler3 should not run
        StopOnFirstFallbackEventHandler1.Reset();
        StopOnFirstFallbackEventHandler2.Reset();
        StopOnFirstFallbackEventHandler3.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Should not throw because Handler2 succeeds
        await mediator.PublishAsync(new StopOnFirstFallbackEvent("test"));

        // Assert
        StopOnFirstFallbackEventHandler1.WasCalled.Should().BeTrue("Handler1 should be tried first");
        StopOnFirstFallbackEventHandler2.WasCalled.Should().BeTrue("Handler2 should be tried after Handler1 fails");
        StopOnFirstFallbackEventHandler3.WasCalled.Should().BeFalse("Handler3 should not run after Handler2 succeeds");
    }

    [Fact]
    public async Task PublishAsync_StopOnFirst_WithStopOnFirstError_ThrowsImmediately()
    {
        // Arrange - StopOnFirstEvent is configured with StopOnFirst + StopOnFirstError (default)
        StopOnFirstEventHandler1.Reset();
        StopOnFirstEventHandler2.Reset();

        var services = new ServiceCollection();
        services.AddTransient<INotificationHandler<StopOnFirstWithErrorEvent>, StopOnFirstWithErrorEventFailingHandler>();
        services.AddTransient<INotificationHandler<StopOnFirstWithErrorEvent>, StopOnFirstWithErrorEventSuccessHandler>();
        services.AddMediatorLite(options =>
        {
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.StopOnFirst;
            options.NotificationErrorStrategy = NotificationErrorStrategy.StopOnFirstError;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should throw immediately because StopOnFirstError
        Func<Task> act = async () => await mediator.PublishAsync(new StopOnFirstWithErrorEvent("test"));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("StopOnFirst handler failed");

        // Success handler should NOT have been called
        StopOnFirstWithErrorEventSuccessHandler.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task PublishAsync_StopOnFirst_AllHandlersFail_ThrowsAggregateException()
    {
        // Arrange - Create notification where all handlers fail with ContinueAndAggregate
        var services = new ServiceCollection();
        services.AddTransient<INotificationHandler<AllFailStopOnFirstEvent>, AllFailStopOnFirstEventHandler1>();
        services.AddTransient<INotificationHandler<AllFailStopOnFirstEvent>, AllFailStopOnFirstEventHandler2>();
        services.AddMediatorLite(options =>
        {
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.StopOnFirst;
            options.NotificationErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should throw AggregateException with all failures
        Func<Task> act = async () => await mediator.PublishAsync(new AllFailStopOnFirstEvent("test"));
        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().HaveCount(2);
    }
}

#region Additional Test Types for StopOnFirst Error Handling

public record StopOnFirstWithErrorEvent(string Message) : INotification;

public class StopOnFirstWithErrorEventFailingHandler : INotificationHandler<StopOnFirstWithErrorEvent>
{
    public ValueTask HandleAsync(StopOnFirstWithErrorEvent notification, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("StopOnFirst handler failed");
    }
}

public class StopOnFirstWithErrorEventSuccessHandler : INotificationHandler<StopOnFirstWithErrorEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(StopOnFirstWithErrorEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.CompletedTask;
    }
}

public record AllFailStopOnFirstEvent(string Message) : INotification;

public class AllFailStopOnFirstEventHandler1 : INotificationHandler<AllFailStopOnFirstEvent>
{
    public ValueTask HandleAsync(AllFailStopOnFirstEvent notification, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Handler1 failed");
    }
}

public class AllFailStopOnFirstEventHandler2 : INotificationHandler<AllFailStopOnFirstEvent>
{
    public ValueTask HandleAsync(AllFailStopOnFirstEvent notification, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Handler2 failed");
    }
}

#endregion
