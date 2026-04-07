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
    public async Task RuntimeBehaviorRegistration_DoesNotAffectPipeline_InV2()
    {
        // Arrange - Register behaviors at runtime
        // In v2, these won't be used because they weren't discovered at compile time
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddTransient<IPipelineBehavior<ComputeValueQuery, int>, AddOneBehavior>();
        services.AddTransient<IPipelineBehavior<ComputeValueQuery, int>, MultiplyByTwoBehavior>();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new ComputeValueQuery(5));

        // Assert - Handler returns 5 * 2 = 10 (behaviors not applied)
        // In v1 this would be 21, but in v2 behaviors must be compile-time discovered
        result.Should().Be(10, "v2 only uses compile-time discovered behaviors");
    }

    /// <summary>
    /// v2: Open generic behaviors registered at runtime are NOT discovered.
    /// They must be in the project at compile time without [MediatorGeneration(Skip=true)].
    /// </summary>
    [Fact]
    public async Task RuntimeOpenGenericBehavior_NotDiscovered_InV2()
    {
        // Arrange
        GenericLoggingBehavior<ComputeValueQuery, int>.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(GenericLoggingBehavior<,>));
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.SendAsync(new ComputeValueQuery(5));

        // Assert - Behavior not called because it has [MediatorGeneration(Skip=true)]
        GenericLoggingBehavior<ComputeValueQuery, int>.Calls.Should().BeEmpty(
            "v2 only executes compile-time discovered behaviors");
    }

    /// <summary>
    /// v2: Short-circuit behaviors must be compile-time discovered to work.
    /// </summary>
    [Fact]
    public async Task RuntimeShortCircuitBehavior_NotApplied_InV2()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddTransient<IPipelineBehavior<ComputeValueQuery, int>, ShortCircuitBehavior>();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new ComputeValueQuery(5));

        // Assert - Handler result, not short-circuit value
        // In v1 this would be 999, but in v2 the behavior is not discovered
        result.Should().Be(10, "v2 only uses compile-time discovered behaviors");
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
    /// v2: Behaviors added via MediatorOptions are NOT used in source-gen pipelines.
    /// MediatorOptions behaviors only work with reflection-based dispatch (deprecated).
    /// </summary>
    [Fact]
    public async Task BehaviorViaOptions_NotApplied_InV2()
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

        // Assert - Behavior not called in v2 source-gen dispatch
        GenericLoggingBehavior<ComputeValueQuery, int>.Calls.Should().BeEmpty(
            "v2 source-gen pipelines don't use MediatorOptions behaviors");
    }

    /// <summary>
    /// Verifies direct handler dispatch without any behaviors (fast path).
    /// </summary>
    [Fact]
    public async Task NoBehaviors_UsesDirectSourceGenDispatch()
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
        
        // Execute the dispatcher directly
        var mediator = provider.GetRequiredService<IMediator>();
        var result = await mediator.SendAsync(new ComputeValueQuery(42));
        result.Should().Be(84, "ComputeValueQuery handler multiplies by 2");
    }
}
