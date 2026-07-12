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
        // Arrange - OrphanEvent has no INotificationHandler<OrphanEvent> in this assembly,
        // so publishing must hit the generated no-handler default arm and complete silently.
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddGeneratedHandlers();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.PublishAsync(new OrphanEvent("nobody listens"));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_ParallelAggregate_HandlerInternalOce_AggregatesAllFaults()
    {
        // Arrange - one parallel handler throws OperationCanceledException for a token that
        // is not the publish token, another sibling throws InvalidOperationException, a third
        // succeeds. ContinueAndAggregate must surface BOTH faults in one AggregateException:
        // the historical bug rethrew the OCE unwrapped and silently dropped the sibling fault.
        ParallelCancellingSiblingHandler.Reset();
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddGeneratedHandlers();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        Func<Task> act = async () => await mediator.PublishAsync(new ParallelCancellingEvent("cancel"));

        // Assert
        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.InnerExceptions.Should().HaveCount(2)
            .And.ContainSingle(e => e is OperationCanceledException)
            .And.ContainSingle(e => e is InvalidOperationException);
        ParallelCancellingSiblingHandler.WasCalled.Should().BeTrue(
            "parallel fan-out starts every handler before awaiting any of them");
    }

    [Fact]
    public async Task PublishAsync_ParallelAggregate_GenuineCancellation_SurfacesOceUnwrapped()
    {
        // Arrange - when the PUBLISH token really is cancelled, cancellation dominates:
        // an unwrapped OperationCanceledException surfaces instead of an AggregateException.
        ParallelCancellingSiblingHandler.Reset();
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddGeneratedHandlers();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await mediator.PublishAsync(new ParallelCancellingEvent("cancel"), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PublishAsync_SequentialAggregate_HandlerInternalOce_DoesNotSkipRemainingHandlers()
    {
        // Arrange - the first sequential handler throws OperationCanceledException for its own
        // internal reason (the publish token is NOT cancelled). ContinueAndAggregate is
        // documented to keep executing all handlers and aggregate the faults; the historical
        // bug rethrew any OCE immediately and skipped the remaining handlers.
        SequentialCancellingSecondHandler.Reset();
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddGeneratedHandlers();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        Func<Task> act = async () => await mediator.PublishAsync(new SequentialCancellingEvent("cancel"));

        // Assert
        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.InnerExceptions.Should().ContainSingle(e => e is OperationCanceledException);
        SequentialCancellingSecondHandler.WasCalled.Should().BeTrue(
            "ContinueAndAggregate must keep executing handlers after a handler-internal cancellation");
    }

    [Fact]
    public async Task PublishAsync_PartialHandlerClass_InvokesHandlerExactlyOnce()
    {
        // Arrange - PartialDeclaredEventHandler is declared in two partial parts, each with
        // a base list. The generator visits every declaration node, so without deduplication
        // the handler would be registered — and invoked — once per part.
        PartialDeclaredEventHandler.Reset();
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddGeneratedHandlers();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.PublishAsync(new PartialHandledEvent("once"));

        // Assert
        PartialDeclaredEventHandler.CallCount.Should().Be(1,
            "a handler split across partial declarations must be discovered once, not once per part");
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
    public async Task PublishAsync_Parallel_WithPreCancelledToken_ThrowsOceWithoutRunningHandlers()
    {
        // Arrange - the handlers are ct-agnostic, so only the mediator's own entry check can
        // reject the pre-cancelled token. Sequential/StopOnFirst already did; Parallel used to
        // run every handler to completion and report success.
        ParallelPreCancelHandler1.Reset();
        ParallelPreCancelHandler2.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        Func<Task> act = async () => await mediator.PublishAsync(new ParallelPreCancelEvent("x"), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        ParallelPreCancelHandler1.WasCalled.Should().BeFalse(
            "no handler may start under an already-cancelled publish token");
        ParallelPreCancelHandler2.WasCalled.Should().BeFalse(
            "no handler may start under an already-cancelled publish token");
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

    [Fact]
    public async Task PublishAsync_ParallelContinueAndAggregate_SyncThrowHandlers_AggregatesAllExceptions()
    {
        // Arrange - ParallelSyncThrowEvent has Parallel+ContinueAndAggregate.
        // Both handlers throw synchronously (bare throw, not ValueTask.FromException).
        // The fix ensures each invocation is wrapped in try/catch so Task.WhenAll sees all faults.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        // Act
        Func<Task> act = async () => await mediator.PublishAsync(new ParallelSyncThrowEvent("test"));

        // Assert - both handlers must have run and their exceptions aggregated
        var ex = await act.Should().ThrowAsync<AggregateException>();
        ex.Which.InnerExceptions.Should().HaveCount(2);
        ex.Which.InnerExceptions.Should()
            .ContainSingle(e => e.Message.Contains("sync-throw-handler-1"))
            .And.ContainSingle(e => e.Message.Contains("sync-throw-handler-2"));
    }

    [Fact]
    public async Task PublishAsync_Parallel_StartPhase_InvokesEveryHandlerBeforeAwaitingAny()
    {
        // Parallel publishing has two phases. This test pins the START PHASE:
        // calling PublishAsync runs the generated Publish_* method synchronously up to its
        // first `await vtN`. Because each handler suspends on a shared gate instead of
        // completing, every handler's synchronous prefix executes here — in start order —
        // before any handler's result is awaited.
        ParallelPhaseProbe.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - invoke, but do NOT release the gate yet.
        var publishTask = mediator.PublishAsync(new ParallelPhaseEvent("test")).AsTask();

        // Assert - both handlers were *started* (in [NotificationHandlerOrder] order) before
        // any was awaited. Sequential execution would show only "h1" here, because h2 would
        // not start until h1 fully completed.
        ParallelPhaseProbe.StartLog.Should().Equal(new[] { "h1", "h2" },
            "the start phase invokes every handler before awaiting any");
        ParallelPhaseProbe.EndLog.Should().BeEmpty("no handler continuation runs until the await phase");
        publishTask.IsCompleted.Should().BeFalse("the publisher is suspended awaiting the in-flight handlers");

        // Cleanup - release the gate so the publish can complete and the task is observed.
        ParallelPhaseProbe.Release();
        await publishTask;
    }

    [Fact]
    public async Task PublishAsync_Parallel_AwaitPhase_ObservesEveryStartedHandlerToCompletion()
    {
        // This test pins the AWAIT PHASE: after the start phase has invoked every handler,
        // the publisher awaits the ValueTasks it already started. Releasing the gate lets
        // those started tasks complete; no new handler is started during the await phase.
        ParallelPhaseProbe.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Start phase ran synchronously inside this call; the publisher is now suspended
        // in its await phase, waiting on the two in-flight handlers.
        var publishTask = mediator.PublishAsync(new ParallelPhaseEvent("test")).AsTask();
        ParallelPhaseProbe.StartLog.Should().Equal("h1", "h2");
        ParallelPhaseProbe.EndLog.Should().BeEmpty();

        // Act - release the gate, unblocking the await phase.
        ParallelPhaseProbe.Release();
        await publishTask;

        // Assert - every started handler was awaited to completion, and no extra handler ran.
        ParallelPhaseProbe.EndLog.Should().HaveCount(2);
        ParallelPhaseProbe.EndLog.Should().Contain("h1").And.Contain("h2");
        ParallelPhaseProbe.StartLog.Should().Equal(new[] { "h1", "h2" },
            "the await phase only observes handlers started during the start phase");
    }
}