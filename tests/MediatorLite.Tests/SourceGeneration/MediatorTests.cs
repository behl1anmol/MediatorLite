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
    // The exact values pin generator discovery over TestTypes.cs: when adding a fixture
    // handler/behavior/validator, update the corresponding count here (rule 70 §4). A `> 0`
    // assertion would keep passing while the generator silently drops discovery of records,
    // partials, or multi-response handlers.
    [Fact]
    public void AddGeneratedHandlers_RegistersRequestHandlers()
    {
        MediatorLiteRegistration.RequestHandlerCount.Should().Be(13,
            "the source generator must discover every request handler in TestTypes.cs");
    }

    [Fact]
    public void AddGeneratedHandlers_RegistersNotificationHandlers()
    {
        MediatorLiteRegistration.NotificationHandlerCount.Should().Be(26,
            "the source generator must discover every notification handler in TestTypes.cs");
    }

    [Fact]
    public void AddGeneratedHandlers_RegistersBehaviors()
    {
        MediatorLiteRegistration.BehaviorCount.Should().Be(31,
            "the source generator must register every discovered behavior (validation behaviors included)");
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
        var mediator = provider.GetService<IMediator>();

        // Assert
        mediator.Should().NotBeNull(
            "AddGeneratedHandlers should register the source-generated IMediator for zero-reflection dispatch");
        mediator.Should().BeOfType<SourceGeneratedMediator>(
            "the generated mediator must win over the AddMediatorLite() diagnostic fallback");
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

        // Assert - dispatch goes through the generated mediator
        mediator.Should().BeOfType<SourceGeneratedMediator>(
            "source-generated dispatch should be used for GetUserByIdQuery");

        // Execute through mediator
        var result = await mediator.SendAsync(new GetUserByIdQuery(1));
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_WithCovariantRequestReference_DispatchesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - dispatch through a covariant IRequest<object> reference (IRequest<out T>),
        // exercising the typed-dispatch fallback cast path instead of the exact-type fast path
        IRequest<object> request = new GetUserByIdQuery(7);
        var result = await mediator.SendAsync(request);

        // Assert
        result.Should().BeOfType<UserDto>();
        ((UserDto)result).Id.Should().Be(7);
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
        services.AddMediatorLite();
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
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Should work with logging enabled
        var result = await mediator.SendAsync(new GetUserByIdQuery(1));
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_RecordDeclaredHandler_IsDiscoveredAndDispatched()
    {
        // Arrange - RecordDeclaredQueryHandler is a `sealed record`, not a class. Records
        // are a distinct syntax node, and the generator used to silently skip them.
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new RecordHandledQuery(1));

        // Assert
        result.Should().Be(101, "the record-declared handler adds 100 to the request value");
    }

    [Fact]
    public async Task SendAsync_RequestWithMultipleResponseTypes_DispatchesEachResponseToItsHandler()
    {
        // Arrange - MultiResponseQuery implements both IRequest<int> and IRequest<string>.
        // The generator used to collapse the request type to a single dispatch arm, so one
        // of the two calls below threw InvalidCastException despite a registered handler.
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var intResult = await mediator.SendAsync<int>(new MultiResponseQuery(5));
        var stringResult = await mediator.SendAsync<string>(new MultiResponseQuery(5));

        // Assert
        intResult.Should().Be(50);
        stringResult.Should().Be("value:5");
    }

    [Fact]
    public async Task SendAsync_MultiResponseWithArrayResponseType_DispatchesWithSanitizedMethodName()
    {
        // Arrange - ArrayItemsQuery is IRequest<int[]> and IRequest<string>. The int[] response
        // display name ("int[]") flows into the generated Send_* method name; unsanitized
        // brackets would emit an invalid identifier and this project would not have compiled.
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var arrayResult = await mediator.SendAsync<int[]>(new ArrayItemsQuery(3));
        var stringResult = await mediator.SendAsync<string>(new ArrayItemsQuery(3));

        // Assert
        arrayResult.Should().Equal(0, 1, 2);
        stringResult.Should().Be("count:3");
    }

    [Fact]
    public async Task SendAsync_CovariantMultiResponseRequest_PicksVarianceCompatiblePipeline()
    {
        // Arrange - MultiResponseQuery implements IRequest<int> and IRequest<string>. An
        // IRequest<object> reference can only exist through the string interface, because
        // IRequest<out T> variance never applies to value types (IRequest<int> is not an
        // IRequest<object>). Dispatch must therefore pick the string pipeline. The int
        // pipeline used to win because the emitted fallback tested
        // typeof(int).IsAssignableTo(typeof(object)), which is true via boxing — returning
        // a boxed 50 instead of "value:5" with no error.
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        IRequest<object> request = new MultiResponseQuery(5);
        var result = await mediator.SendAsync(request);

        // Assert
        result.Should().Be("value:5");
    }
}
