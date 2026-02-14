using FluentAssertions;
using MediatorLite.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediatorLite.Tests.SourceGeneration;

/// <summary>
/// Tests for pipeline behavior functionality when using source-generated handler registration.
/// These tests verify that behaviors work correctly with source-generated dispatch.
/// </summary>
public class PipelineBehaviorTests
{
    [Fact]
    public async Task Behaviors_ExecuteInRegistrationOrder_WithSourceGen()
    {
        // Arrange
        // Handler: 5 * 2 = 10
        // MultiplyByTwoBehavior: 10 * 2 = 20
        // AddOneBehavior: 20 + 1 = 21
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddTransient<IPipelineBehavior<ComputeValueQuery, int>, AddOneBehavior>();
        services.AddTransient<IPipelineBehavior<ComputeValueQuery, int>, MultiplyByTwoBehavior>();
        services.AddMediatorLiteCore();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new ComputeValueQuery(5));

        // Assert
        result.Should().Be(21);
    }

    [Fact]
    public async Task OpenGenericBehavior_IsApplied_WithSourceGen()
    {
        // Arrange
        GenericLoggingBehavior<ComputeValueQuery, int>.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(GenericLoggingBehavior<,>));
        services.AddMediatorLiteCore();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.SendAsync(new ComputeValueQuery(5));

        // Assert
        GenericLoggingBehavior<ComputeValueQuery, int>.Calls.Should().Contain("Before: ComputeValueQuery");
        GenericLoggingBehavior<ComputeValueQuery, int>.Calls.Should().Contain("After: ComputeValueQuery");
    }

    [Fact]
    public async Task Behavior_CanShortCircuit_WithSourceGen()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddTransient<IPipelineBehavior<ComputeValueQuery, int>, ShortCircuitBehavior>();
        services.AddMediatorLiteCore();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new ComputeValueQuery(5));

        // Assert
        result.Should().Be(999); // Short-circuit value, not the handler result
    }

    [Fact]
    public async Task Behavior_UsesSourceGenForInnerHandler()
    {
        // This test verifies that even when behaviors are present,
        // the innermost handler invocation uses source-generated dispatch

        // Arrange
        GenericLoggingBehavior<GetUserByIdQuery, UserDto>.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(GenericLoggingBehavior<,>));
        services.AddMediatorLiteCore();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var sourceGenMediator = provider.GetRequiredService<ISourceGeneratedMediator>();

        // Verify source-gen can handle this request type
        var canDispatch = sourceGenMediator.TryInvokeHandlerAsync<UserDto>(
            provider,
            new GetUserByIdQuery(1),
            CancellationToken.None);
        canDispatch.Should().NotBeNull("TryInvokeHandlerAsync should recognize GetUserByIdQuery");

        // Act - Execute through mediator with behavior
        var result = await mediator.SendAsync(new GetUserByIdQuery(42));

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(42);
        GenericLoggingBehavior<GetUserByIdQuery, UserDto>.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task MultipleBehaviors_ExecuteCorrectly_WithSourceGen()
    {
        // Arrange - Multiple behaviors on same request
        GenericLoggingBehavior<ComputeValueQuery, int>.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(GenericLoggingBehavior<,>));
        services.AddTransient<IPipelineBehavior<ComputeValueQuery, int>, AddOneBehavior>();
        services.AddMediatorLiteCore();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        // Handler: 5 * 2 = 10
        // AddOneBehavior: 10 + 1 = 11
        // GenericLoggingBehavior: just logs, returns 11
        var result = await mediator.SendAsync(new ComputeValueQuery(5));

        // Assert
        result.Should().Be(11);
        GenericLoggingBehavior<ComputeValueQuery, int>.Calls.Should().Contain("Before: ComputeValueQuery");
        GenericLoggingBehavior<ComputeValueQuery, int>.Calls.Should().Contain("After: ComputeValueQuery");
    }

    [Fact]
    public async Task Behavior_WithException_PropagatesCorrectly()
    {
        // Arrange
        GenericLoggingBehavior<FailingRequest, string>.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(GenericLoggingBehavior<,>));
        services.AddMediatorLiteCore();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.SendAsync(new FailingRequest());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Handler failed*");

        // Behavior should have logged the "Before" but not "After" due to exception
        GenericLoggingBehavior<FailingRequest, string>.Calls.Should().Contain("Before: FailingRequest");
    }

    [Fact]
    public async Task BehaviorAddedViaOptions_WorksWithSourceGen()
    {
        // Arrange
        GenericLoggingBehavior<ComputeValueQuery, int>.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite(options =>
        {
            options.AddOpenBehavior(typeof(GenericLoggingBehavior<,>));
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.SendAsync(new ComputeValueQuery(5));

        // Assert
        GenericLoggingBehavior<ComputeValueQuery, int>.Calls.Should().Contain("Before: ComputeValueQuery");
        GenericLoggingBehavior<ComputeValueQuery, int>.Calls.Should().Contain("After: ComputeValueQuery");
    }

    [Fact]
    public async Task NoBehaviors_UsesDirectSourceGenDispatch()
    {
        // Arrange - No behaviors registered
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLiteCore();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Should use fast path (no behaviors)
        var result = await mediator.SendAsync(new GetUserByIdQuery(99));

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(99);
    }

    [Fact]
    public async Task SourceGenDispatch_ResolvesHandlerCorrectly()
    {
        // Verify that source-gen dispatch resolves the handler from DI (not just calling it directly)
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLiteCore();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var sourceGenMediator = provider.GetRequiredService<ISourceGeneratedMediator>();

        // Verify TryResolveBehaviors returns empty list (not null) for known types with no behaviors
        var behaviors = sourceGenMediator.TryResolveBehaviors(
            provider, typeof(ComputeValueQuery), typeof(int));
        behaviors.Should().NotBeNull("TryResolveBehaviors should recognize ComputeValueQuery");
        behaviors!.Count.Should().Be(0);
    }
}
