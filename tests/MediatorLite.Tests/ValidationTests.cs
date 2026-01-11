using FluentAssertions;
using MediatorLite.Validation;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using Xunit;
using MediatorValidationResult = MediatorLite.Validation.ValidationResult;

namespace MediatorLite.Tests;

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

    public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, int>
    {
        public ValueTask<int> HandleAsync(CreateUserCommand request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(42);
        }
    }

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
    public async Task ValidationBehavior_WithValidRequest_PassesThrough()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<CreateUserCommand, int>, CreateUserCommandHandler>();
        services.AddTransient<IValidator<CreateUserCommand>, DataAnnotationsValidator<CreateUserCommand>>();
        services.AddTransient<IPipelineBehavior<CreateUserCommand, int>, ValidationBehavior<CreateUserCommand, int>>();
        services.AddMediatorLite(options =>
        {
            options.AddBehavior<ValidationBehavior<CreateUserCommand, int>>();
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new CreateUserCommand("John", "john@example.com"));

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public async Task ValidationBehavior_WithInvalidRequest_ThrowsValidationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<CreateUserCommand, int>, CreateUserCommandHandler>();
        services.AddTransient<IValidator<CreateUserCommand>, DataAnnotationsValidator<CreateUserCommand>>();
        services.AddTransient<IPipelineBehavior<CreateUserCommand, int>, ValidationBehavior<CreateUserCommand, int>>();
        services.AddMediatorLite(options =>
        {
            options.AddBehavior<ValidationBehavior<CreateUserCommand, int>>();
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.SendAsync(new CreateUserCommand("J", "invalid-email"));
        await act.Should().ThrowAsync<MediatorLite.Validation.ValidationException>();
    }

    [Fact]
    public async Task CustomValidator_IsInvoked()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<CreateUserCommand, int>, CreateUserCommandHandler>();
        services.AddTransient<IValidator<CreateUserCommand>, CustomValidator>();
        services.AddTransient<IPipelineBehavior<CreateUserCommand, int>, ValidationBehavior<CreateUserCommand, int>>();
        services.AddMediatorLite(options =>
        {
            options.AddBehavior<ValidationBehavior<CreateUserCommand, int>>();
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.SendAsync(new CreateUserCommand("admin_user", "admin@example.com"));
        var exception = await act.Should().ThrowAsync<MediatorLite.Validation.ValidationException>();
        exception.Which.Errors.Should().Contain(e => e.ErrorMessage.Contains("admin"));
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
