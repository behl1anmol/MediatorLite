using FluentAssertions;
using MediatorLite.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediatorLite.Tests.SourceGeneration;

/// <summary>
/// Tests for pipeline behavior functionality.
/// Behaviors are discovered at compile-time to be included in the unrolled pipeline.
/// </summary>
public class PipelineBehaviorTests
{
    /// <summary>
    /// Verifies that a request with multiple compile-time discovered behaviors executes all behaviors and the handler correctly.
    /// </summary>
    [Fact]
    public async Task ComputeValueQueryBehaviorExecution_ReturnsCorrectResult()
    {
        // Arrange - Register behaviors at runtime
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddGeneratedHandlers();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new ComputeValueQuery(5));

        // Assert - Handler returns 5 * 2 = 10; MultiplyByTwoBehavior (order 2, inner) doubles
        // it to 20; AddOneBehavior (order 1, outer) adds 1 → 21.
        result.Should().Be(21, "All compile-time discovered behaviors execute successfully with handlers");
    }

    /// <summary>
    /// Verifies that a short-circuiting behavior prevents subsequent behaviors and the handler from executing.
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

        // Verify dispatch goes through the generated mediator
        mediator.Should().BeOfType<MediatorLite.Generated.SourceGeneratedMediator>();

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
    public async Task OpenBehaviors_UsesDirectSourceGenDispatch()
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
        // Verify that source-gen dispatch recognizes the request type
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // A recognized request type dispatches without throwing InvalidOperationException
        var act = async () => await mediator.SendAsync(new ComputeValueQuery(21));
        await act.Should().NotThrowAsync("source-generated dispatch should recognize ComputeValueQuery");
    }
}
