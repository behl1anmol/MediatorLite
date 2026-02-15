using FluentAssertions;
using MediatorLite.Generated;
using MediatorLite.Validation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediatorLite.Tests.SourceGeneration;

/// <summary>
/// Tests for source-generated validation behavior registration and execution order.
/// Verifies that ValidationBehavior runs first in the pipeline, short-circuits on failure,
/// and that validators are auto-discovered by the source generator.
/// </summary>
public class ValidationTests
{
    [Fact]
    public async Task ValidationBehavior_ExecutesBeforeOtherBehaviors_WithSourceGen()
    {
        // Arrange
        ValidatedCommandHandler.Reset();
        ValidatedCommandCustomValidator.Reset();
        ExecutionOrderTrackingBehavior<ValidatedCommand, string>.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        // Add tracking behavior AFTER source-gen registration.
        // ValidationBehavior is registered first by AddGeneratedBehaviors(),
        // so it will execute before this tracking behavior.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ExecutionOrderTrackingBehavior<,>));
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Send a valid request
        var result = await mediator.SendAsync(new ValidatedCommand { Name = "ValidName", Value = 42 });

        // Assert - Both validation and tracking behavior ran, handler executed
        result.Should().Be("Processed: ValidName");
        ValidatedCommandHandler.WasExecuted.Should().BeTrue();
        ValidatedCommandCustomValidator.WasExecuted.Should().BeTrue();
        ExecutionOrderTrackingBehavior<ValidatedCommand, string>.ExecutionLog
            .Should().Contain("TrackingBehavior:Before:ValidatedCommand");
    }

    [Fact]
    public async Task ValidationBehavior_ShortCircuitsOnDataAnnotationFailure_BeforeOtherBehaviors()
    {
        // Arrange
        ValidatedCommandHandler.Reset();
        ValidatedCommandCustomValidator.Reset();
        ExecutionOrderTrackingBehavior<ValidatedCommand, string>.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ExecutionOrderTrackingBehavior<,>));
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Send invalid request (Name too short, Value out of range)
        Func<Task> act = async () => await mediator.SendAsync(
            new ValidatedCommand { Name = "A", Value = 0 });

        // Assert - ValidationException thrown, handler and tracking behavior NOT executed
        await act.Should().ThrowAsync<ValidationException>();
        ValidatedCommandHandler.WasExecuted.Should().BeFalse(
            "handler should not execute when validation fails");
        ExecutionOrderTrackingBehavior<ValidatedCommand, string>.ExecutionLog
            .Should().BeEmpty("tracking behavior should not execute when validation short-circuits");
    }

    [Fact]
    public async Task ValidationBehavior_ShortCircuitsOnCustomValidatorFailure_BeforeOtherBehaviors()
    {
        // Arrange
        ValidatedCommandHandler.Reset();
        ValidatedCommandCustomValidator.Reset();
        ExecutionOrderTrackingBehavior<ValidatedCommand, string>.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ExecutionOrderTrackingBehavior<,>));
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Send request that passes DataAnnotations but fails custom validator
        Func<Task> act = async () => await mediator.SendAsync(
            new ValidatedCommand { Name = "blocked_item", Value = 50 });

        // Assert - Custom validator should have failed, short-circuiting the pipeline
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(e => e.ErrorMessage.Contains("blocked"));
        ValidatedCommandHandler.WasExecuted.Should().BeFalse(
            "handler should not execute when custom validation fails");
        ExecutionOrderTrackingBehavior<ValidatedCommand, string>.ExecutionLog
            .Should().BeEmpty("tracking behavior should not execute when validation short-circuits");
    }

    [Fact]
    public async Task SourceGen_AutoRegistersDataAnnotationsValidator()
    {
        // Arrange - Only use AddGeneratedHandlers(), no manual validator registration
        ValidatedCommandHandler.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Send request that violates DataAnnotation rules
        Func<Task> act = async () => await mediator.SendAsync(
            new ValidatedCommand { Name = "X", Value = 200 });

        // Assert - DataAnnotations validation should fail (auto-registered by source gen)
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().HaveCountGreaterThan(0);
        ValidatedCommandHandler.WasExecuted.Should().BeFalse();
    }

    [Fact]
    public async Task SourceGen_AutoRegistersCustomValidator()
    {
        // Arrange - Only use AddGeneratedHandlers(), no manual validator registration
        ValidatedCommandCustomValidator.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Send valid request to verify custom validator runs
        await mediator.SendAsync(new ValidatedCommand { Name = "ValidName", Value = 42 });

        // Assert - Custom validator was called (auto-registered by source gen)
        ValidatedCommandCustomValidator.WasExecuted.Should().BeTrue(
            "source generator should auto-register custom validators implementing IValidator<T>");
    }

    [Fact]
    public async Task ValidRequest_PassesThroughAllBehaviors()
    {
        // Arrange
        ValidatedCommandHandler.Reset();
        ValidatedCommandCustomValidator.Reset();
        ExecutionOrderTrackingBehavior<ValidatedCommand, string>.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ExecutionOrderTrackingBehavior<,>));
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new ValidatedCommand { Name = "Good", Value = 50 });

        // Assert - All behaviors and handler ran in correct order
        result.Should().Be("Processed: Good");
        ValidatedCommandHandler.WasExecuted.Should().BeTrue();
        ValidatedCommandCustomValidator.WasExecuted.Should().BeTrue();
        ExecutionOrderTrackingBehavior<ValidatedCommand, string>.ExecutionLog
            .Should().ContainInOrder(
                "TrackingBehavior:Before:ValidatedCommand",
                "TrackingBehavior:After:ValidatedCommand");
    }

    [Fact]
    public void SourceGen_ReportsValidatorCount()
    {
        // Assert - Source generator should report discovered validators
        MediatorLiteRegistration.ValidatorCount.Should().BeGreaterThan(0,
            "source generator should discover validators at compile time");
    }

    [Fact]
    public async Task MultipleValidationErrors_AreAggregated()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Send request with multiple DataAnnotation violations
        Func<Task> act = async () => await mediator.SendAsync(
            new ValidatedCommand { Name = "X", Value = 200 });

        // Assert - All validation errors aggregated
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().HaveCountGreaterThanOrEqualTo(2,
            "both Name length and Value range violations should be reported");
    }
}
