using FluentAssertions;
using MediatorLite.Validation;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using MediatorLite.Validation.Models;
using Xunit;
using MediatorValidationResult = MediatorLite.Validation.Models.ValidationResult;

namespace MediatorLite.Tests.Reflection;

/// <summary>
/// Tests for validation behavior with manual DI registration (no source generation).
/// 
/// IMPORTANT (v2 Architecture):
/// v2 deprecates the reflection fallback path. Handlers with [MediatorGeneration(Skip = true)]
/// are not discovered at compile time, so no dispatcher is generated for them.
/// When sending a request with no source-generated dispatcher, v2 throws InvalidOperationException.
/// 
/// These tests verify that v2 correctly rejects manually-registered handlers that weren't
/// discovered at compile time.
/// </summary>
public class ValidationTests
{
    #region Test Types

    public record CreateUserCommand(
        [property: Required]
        [property: MinLength(2)]
        string Name,

        [property: Required]
        [property: EmailAddress]
        string Email) : IRequest<int>;

    [MediatorGeneration(Skip = true)]
    public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, int>
    {
        public ValueTask<int> HandleAsync(CreateUserCommand request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(42);
        }
    }

    [MediatorGeneration(Skip = true)]
    public class CustomValidator : IValidator<CreateUserCommand>
    {
        public ValueTask<MediatorValidationResult> ValidateAsync(CreateUserCommand request, CancellationToken cancellationToken = default)
        {
            if (request.Name.Contains("admin", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(MediatorValidationResult.Failure(
                    new ValidationError("Name", "Name cannot contain 'admin'")));
            }

            return ValueTask.FromResult(MediatorValidationResult.Success);
        }
    }

    #endregion

    [Fact]
    public async Task ManuallyRegisteredHandler_ThrowsInv2_NoSourceGenDispatcher()
    {
        // Arrange - Handler has [MediatorGeneration(Skip = true)], so no dispatcher generated
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<CreateUserCommand, int>, CreateUserCommandHandler>();
        services.AddTransient<IValidator<CreateUserCommand>, DataAnnotationsValidator<CreateUserCommand>>();
        services.AddTransient<IPipelineBehavior<CreateUserCommand, int>, ValidationBehavior<CreateUserCommand, int>>();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - v2 throws because no source-gen dispatcher exists for this request type
        Func<Task> act = async () => await mediator.SendAsync(new CreateUserCommand("John", "john@example.com"));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No handler*");
    }

    [Fact]
    public async Task V2Architecture_RequiresSourceGeneration_ForDispatch()
    {
        // This test documents v2's source-gen-only architecture.
        // Handlers marked with [MediatorGeneration(Skip = true)] are NOT discovered,
        // so manually registering them in DI does NOT make them dispatchable.
        
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<CreateUserCommand, int>, CreateUserCommandHandler>();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.SendAsync(new CreateUserCommand("Test", "test@test.com"));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CustomValidator_WithManualRegistration_NotReachable()
    {
        // In v2, manually registered handlers are not dispatchable
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<CreateUserCommand, int>, CreateUserCommandHandler>();
        services.AddTransient<IValidator<CreateUserCommand>, CustomValidator>();
        services.AddTransient<IPipelineBehavior<CreateUserCommand, int>, ValidationBehavior<CreateUserCommand, int>>();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - Throws because no source-gen dispatcher (validation never reached)
        Func<Task> act = async () => await mediator.SendAsync(new CreateUserCommand("admin_user", "admin@example.com"));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void ValidationResult_Success_IsValid()
    {
        // Arrange & Act & Assert
        MediatorValidationResult.Success.IsValid.Should().BeTrue();
        MediatorValidationResult.Success.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidationResult_Failure_ContainsErrors()
    {
        // Arrange & Act
        var result = MediatorValidationResult.Failure(
            new ValidationError("Field1", "Error1"),
            new ValidationError("Field2", "Error2"));

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void ValidationException_Message_ContainsErrors()
    {
        // Arrange
        var errors = new[]
        {
            new ValidationError("Name", "Name is required"),
            new ValidationError("Email", "Email is invalid")
        };

        // Act
        var exception = new MediatorLite.Validation.ValidationException(errors);

        // Assert
        exception.Message.Should().Contain("2 errors");
        exception.Errors.Should().HaveCount(2);
    }
}
