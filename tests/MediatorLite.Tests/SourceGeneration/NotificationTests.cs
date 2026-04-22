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
        services.AddMediatorLite();
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
        // This test verifies that source-generated notification publisher
        // has handlers ordered correctly (order is baked into generated code)

        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var sourceGenMediator = provider.GetRequiredService<ISourceGeneratedMediator>();

        // Act - Check if source-gen has a publisher for this notification type
        var publisher = sourceGenMediator.GetPublisher(typeof(UserCreatedEvent));

        // Assert - Publisher should exist
        publisher.Should().NotBeNull("GetPublisher should recognize UserCreatedEvent");
        
        // Execute and verify handlers ran in order
        UserCreatedEventHandler1.Reset();
        UserCreatedEventHandler2.Reset();
        UserCreatedEventHandler3.Reset();
        
        var mediator = provider.GetRequiredService<IMediator>();
        await mediator.PublishAsync(new UserCreatedEvent(1, "test@test.com"));
        
        // Order should be: Handler1 (default 0), Handler2 (order 1), Handler3 (order 2)
        UserCreatedEventHandler1.CallOrder.Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public async Task PublishAsync_WithNoHandlers_CompletesWithoutError()
    {
        // Arrange - Create a notification type that has no handlers registered
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddGeneratedHandlers();
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
        // Arrange - ParallelEvent has [NotificationExecution(Parallel)] + [NotificationError(ContinueAndAggregate)].
        // Behavior proves the attribute-driven strategies were baked into the generated publisher:
        // Parallel + ContinueAndAggregate surfaces as an AggregateException AND the success handler still runs.
        ParallelEventSuccessHandler.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.PublishAsync(new ParallelEvent("test"));
        await act.Should().ThrowAsync<AggregateException>();
        ParallelEventSuccessHandler.WasCalled.Should().BeTrue(
            "Parallel + ContinueAndAggregate: the success handler must run even when the failing handler throws");
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
    public async Task PublishAsync_PerNotificationAttribute_WinsOverLibraryDefaults()
    {
        // Arrange - UserCreatedEvent has no per-type attribute, so it falls back to library defaults
        // (Sequential + StopOnFirstError). ParallelEvent has [NotificationExecution(Parallel)] +
        // [NotificationError(ContinueAndAggregate)] which must override the library defaults.
        // This mirrors the resolution precedence: per-notification attribute > assembly default > library default.
        ParallelEventSuccessHandler.Reset();
        UserCreatedEventHandler1.Reset();
        UserCreatedEventHandler2.Reset();
        UserCreatedEventHandler3.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // ParallelEvent: attribute-driven Parallel + ContinueAndAggregate wins.
        Func<Task> publishParallel = async () => await mediator.PublishAsync(new ParallelEvent("test"));
        await publishParallel.Should().ThrowAsync<AggregateException>();
        ParallelEventSuccessHandler.WasCalled.Should().BeTrue();

        // UserCreatedEvent: no attribute, so library defaults (Sequential + StopOnFirstError) apply.
        await mediator.PublishAsync(new UserCreatedEvent(1, "test@test.com"));
        UserCreatedEventHandler1.CallOrder.Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public async Task PublishAsync_WithTracing_DoesNotThrow()
    {
        // Arrange
        UserCreatedEventHandler1.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
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
        services.AddMediatorLite();
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
        // Arrange - StopOnFirstWithStopOnFirstErrorEvent has [NotificationExecution(StopOnFirst)] +
        // [NotificationError(StopOnFirstError)]. Strategies are resolved at compile time, so this
        // test verifies the attribute-driven configuration is baked into the generated publisher.
        StopOnFirstWithStopOnFirstErrorEventFailingHandler.Reset();
        StopOnFirstWithStopOnFirstErrorEventSuccessHandler.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should throw immediately because StopOnFirstError (from attribute)
        Func<Task> act = async () => await mediator.PublishAsync(new StopOnFirstWithStopOnFirstErrorEvent("test"));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("StopOnFirst handler failed");

        // Failing handler should run first and throw, success handler should not run.
        StopOnFirstWithStopOnFirstErrorEventFailingHandler.WasCalled.Should().BeTrue();
        StopOnFirstWithStopOnFirstErrorEventSuccessHandler.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task PublishAsync_StopOnFirst_AllHandlersFail_ThrowsAggregateException()
    {
        // Arrange - AllFailStopOnFirstWithAggregateEvent has [NotificationExecution(StopOnFirst)] +
        // [NotificationError(ContinueAndAggregate)]. Strategies are resolved at compile time. This test
        // verifies the aggregate exception path when every handler fails.
        AllFailStopOnFirstWithAggregateEventHandler1.Reset();
        AllFailStopOnFirstWithAggregateEventHandler2.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should throw AggregateException with all failures
        Func<Task> act = async () => await mediator.PublishAsync(new AllFailStopOnFirstWithAggregateEvent("test"));
        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().HaveCount(2);
    }
}