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
        services.AddMediatorLiteCore();
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
        services.AddMediatorLiteCore();
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
        services.AddMediatorLiteCore();
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
        services.AddMediatorLiteCore();
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
        services.AddMediatorLiteCore();
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
        services.AddMediatorLiteCore();
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
        services.AddMediatorLiteCore();
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
}
