using FluentAssertions;
using MediatorLite.Configuration;
using MediatorLite.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MediatorLite.Tests.Reflection;

public class MediatorTests
{
    #region Test Request/Handler Types

    public record GetUserQuery(int Id) : IRequest<User>;

    public record User(int Id, string Name);

    public class GetUserQueryHandler : IRequestHandler<GetUserQuery, User>
    {
        public ValueTask<User> HandleAsync(GetUserQuery request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new User(request.Id, "Test User"));
        }
    }

    public record DeleteUserCommand(int Id) : IRequest;

    public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
    {
        public bool WasCalled { get; private set; }

        public ValueTask HandleAsync(DeleteUserCommand request, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    public record FailingQuery : IRequest<string>;

    public class FailingQueryHandler : IRequestHandler<FailingQuery, string>
    {
        public ValueTask<string> HandleAsync(FailingQuery request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Handler failed intentionally");
        }
    }

    #endregion

    [Fact]
    public async Task SendAsync_WithValidRequest_ReturnsResponse()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediatorLite(options =>
        {
            options.RegisterHandlersFromAssemblyContaining<MediatorTests>();
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new GetUserQuery(42));

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(42);
        result.Name.Should().Be("Test User");
    }

    [Fact]
    public async Task SendAsync_WithVoidRequest_CompletesSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediatorLite(options =>
        {
            options.RegisterHandlersFromAssemblyContaining<MediatorTests>();
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new DeleteUserCommand(1));

        // Assert
        result.Should().Be(Unit.Value);
    }

    [Fact]
    public async Task SendAsync_WithNoRegisteredHandler_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediatorLite(); // No handlers registered
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.SendAsync(new GetUserQuery(1));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No handler registered*");
    }

    [Fact]
    public async Task SendAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.SendAsync<User>(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendAsync_WhenHandlerThrows_PropagatesException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediatorLite(options =>
        {
            options.RegisterHandlersFromAssemblyContaining<MediatorTests>();
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.SendAsync(new FailingQuery());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Handler failed*");
    }

    [Fact]
    public async Task SendAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<DelayedQuery, string>, DelayedQueryHandler>();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        Func<Task> act = async () => await mediator.SendAsync(new DelayedQuery(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    public record DelayedQuery : IRequest<string>;

    public class DelayedQueryHandler : IRequestHandler<DelayedQuery, string>
    {
        public async ValueTask<string> HandleAsync(DelayedQuery request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(1000, cancellationToken);
            return "Done";
        }
    }
}

public class UnitTests
{
    [Fact]
    public void Unit_Value_ReturnsSingleton()
    {
        // Arrange & Act
        var unit1 = Unit.Value;
        var unit2 = Unit.Value;

        // Assert
        unit1.Should().Be(unit2);
    }

    [Fact]
    public void Unit_CompareTo_ReturnsZero()
    {
        // Arrange
        var unit1 = Unit.Value;
        var unit2 = Unit.Value;

        // Act & Assert
        unit1.CompareTo(unit2).Should().Be(0);
    }

    [Fact]
    public void Unit_ToString_ReturnsParentheses()
    {
        // Arrange & Act & Assert
        Unit.Value.ToString().Should().Be("()");
    }

    [Fact]
    public void Unit_CompletedTask_IsCompleted()
    {
        // Arrange & Act & Assert
        Unit.CompletedTask.IsCompleted.Should().BeTrue();
    }
}
