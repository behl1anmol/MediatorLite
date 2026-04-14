using FluentAssertions;
using MediatorLite.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediatorLite.Tests.SourceGeneration;

/// <summary>
/// Tests for pipeline behavior functionality when using source-generated handler registration.
/// 
/// IMPORTANT v2 CHANGE: Behaviors must be discovered at compile-time to be included in the
/// unrolled pipeline. Runtime registration via DI does NOT affect the pipeline.
/// 
/// Behaviors with [MediatorGeneration(Skip = true)] will NOT be discovered and thus
/// will NOT be part of the generated pipeline, even if registered in DI.
/// </summary>
public class PipelineBehaviorTests
{
    /// <summary>
    /// v2: This test documents that runtime behavior registration does NOT affect the pipeline.
    /// Since test behaviors have [MediatorGeneration(Skip=true)], they are not discovered
    /// at compile time and therefore not included in the unrolled pipeline.
    /// </summary>
    [Fact]
    public async Task ComputeValueQueryBehaviorExecution_ReturnsCorrectResult()
    {
        // Arrange - Register behaviors at runtime
        // In v2, these won't be used because they weren't discovered at compile time
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddGeneratedHandlers();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new ComputeValueQuery(5));

        // Assert - Handler returns 5 * 2 = 10 (behaviors not applied)
        result.Should().Be(21, "All compile-time discovered behaviors execute successfully with handlers");
    }

    /// <summary>
    /// v2: Short-circuit behaviors must be compile-time discovered to work.
    /// </summary>
    [Fact]
    public async Task ShortCircuitBehavior_ShotCircuitSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new ShortCircuitQuery());

        result.Should().BeOfType<Unit>();
        Assert.True(ShortCircuitBehavior.Executed);
        Assert.False(ShortCircuitLoggerBehavior.Executed);

    }

    /// <summary>
    /// Verifies that source-gen dispatch works correctly without behaviors.
    /// </summary>
    [Fact]
    public async Task SourceGenDispatch_WorksWithoutBehaviors()
    {
        // Arrange
        GenericLoggingBehavior<GetUserByIdQuery, UserDto>.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var sourceGenMediator = provider.GetRequiredService<ISourceGeneratedMediator>();

        // Verify source-gen has a dispatcher for this request type
        var dispatcher = sourceGenMediator.GetDispatcher(typeof(GetUserByIdQuery));
        dispatcher.Should().NotBeNull("GetDispatcher should recognize GetUserByIdQuery");

        // Act - Execute through mediator
        var result = await mediator.SendAsync(new GetUserByIdQuery(42));

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(42);
    }

    /// <summary>
    /// Verifies that exceptions from handlers propagate correctly.
    /// </summary>
    [Fact]
    public async Task Handler_WithException_PropagatesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.SendAsync(new FailingRequest());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Handler failed*");
    }

    /// <summary>
    /// Verifies direct handler dispatch without any behaviors (fast path).
    /// </summary>
    [Fact]
    public async Task OpenBenaviors_UsesDirectSourceGenDispatch()
    {
        // Arrange - No behaviors registered
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Should use fast path (no behaviors)
        var result = await mediator.SendAsync(new GetUserByIdQuery(99));

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(99);
    }

    /// <summary>
    /// Verifies that source-gen dispatch recognizes request types.
    /// </summary>
    [Fact]
    public async Task SourceGenDispatch_RecognizesRequestTypes()
    {
        // Verify that source-gen dispatch has a dispatcher for the request type
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var sourceGenMediator = provider.GetRequiredService<ISourceGeneratedMediator>();

        // Verify GetDispatcher returns a dispatcher for known request types
        var dispatcher = sourceGenMediator.GetDispatcher(typeof(ComputeValueQuery));
        dispatcher.Should().NotBeNull("GetDispatcher should recognize ComputeValueQuery");
    }
}
