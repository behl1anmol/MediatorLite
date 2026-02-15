using FluentAssertions;
using MediatorLite.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediatorLite.Tests.SourceGeneration;

/// <summary>
/// Tests for mediator functionality when using source-generated handler registration.
/// These tests verify that <see cref="MediatorLiteRegistration.AddGeneratedHandlers"/> 
/// properly discovers and registers handlers at compile-time for zero-reflection dispatch.
/// </summary>
public class MediatorTests
{
    [Fact]
    public void AddGeneratedHandlers_RegistersRequestHandlers()
    {
        // Assert that source generator discovered our handlers
        MediatorLiteRegistration.RequestHandlerCount.Should().BeGreaterThan(0,
            "Source generator should discover request handlers at compile-time");
    }

    [Fact]
    public void AddGeneratedHandlers_RegistersNotificationHandlers()
    {
        // Assert that source generator discovered notification handlers
        MediatorLiteRegistration.NotificationHandlerCount.Should().BeGreaterThan(0,
            "Source generator should discover notification handlers at compile-time");
    }

    [Fact]
    public void AddGeneratedHandlers_RegistersSourceGeneratedMediator()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();

        // Act
        var sourceGenMediator = provider.GetService<ISourceGeneratedMediator>();

        // Assert
        sourceGenMediator.Should().NotBeNull(
            "AddGeneratedHandlers should register ISourceGeneratedMediator for zero-reflection dispatch");
    }

    [Fact]
    public async Task SendAsync_WithSourceGeneration_ReturnsResponse()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new GetUserByIdQuery(42));

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(42);
        result.Name.Should().Be("Test User");
    }

    [Fact]
    public async Task SendAsync_WithVoidRequest_CompletesSuccessfully()
    {
        // Arrange
        DeleteUserByIdCommandHandler.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new DeleteUserByIdCommand(123));

        // Assert
        result.Should().Be(Unit.Value);
        DeleteUserByIdCommandHandler.WasCalled.Should().BeTrue();
        DeleteUserByIdCommandHandler.LastDeletedId.Should().Be(123);
    }

    [Fact]
    public async Task SendAsync_WithSourceGeneration_PropagatesException()
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

    [Fact]
    public async Task SendAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - DelayedRequest handler checks cancellation token
        Func<Task> act = async () => await mediator.SendAsync(new DelayedRequest(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SendAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.SendAsync<UserDto>(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendAsync_SourceGenDispatch_IsUsed()
    {
        // Arrange - Use source-gen registration
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var sourceGenMediator = provider.GetService<ISourceGeneratedMediator>();

        // Verify source-gen mediator can dispatch our request type
        var canDispatch = sourceGenMediator?.TrySendAsync<UserDto>(
            provider,
            new GetUserByIdQuery(1),
            CancellationToken.None);

        // Assert
        canDispatch.Should().NotBeNull("Source-generated mediator should recognize GetUserByIdQuery");

        // Execute through mediator
        var result = await mediator.SendAsync(new GetUserByIdQuery(1));
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MultipleRequests_AllUseSourceGeneration()
    {
        // Arrange
        CreateUserCommandHandler.Reset();
        DeleteUserByIdCommandHandler.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Execute multiple different request types
        var user = await mediator.SendAsync(new GetUserByIdQuery(1));
        var createdId = await mediator.SendAsync(new CreateUserCommand("Test", "test@example.com"));
        await mediator.SendAsync(new DeleteUserByIdCommand(999));

        // Assert
        user.Should().NotBeNull();
        createdId.Should().BeGreaterThan(0);
        DeleteUserByIdCommandHandler.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WithTracingEnabled_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite(options =>
        {
            options.EnableTracing = true;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should work with tracing enabled
        var result = await mediator.SendAsync(new GetUserByIdQuery(1));
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_WithLoggingEnabled_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite(options =>
        {
            options.EnableBuiltInLogging = true;
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should work with logging enabled
        var result = await mediator.SendAsync(new GetUserByIdQuery(1));
        result.Should().NotBeNull();
    }
}
