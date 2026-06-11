using FluentAssertions;
using FluentValidation;
using MediatorLite;
using MediatorLite.FluentValidation;
using Xunit;
using ValidationException = MediatorLite.Validation.ValidationException;

namespace MediatorLite.Tests.UnitTests;

/// <summary>
/// Unit tests for <see cref="FluentValidationBehavior{TRequest, TResponse}"/> in isolation
/// (no source generator): verifies the no-validator and all-valid paths call next(), and that
/// FluentValidation failures are mapped to a MediatorLite <see cref="ValidationException"/>.
/// </summary>
public class FluentValidationBehaviorTests
{
    public sealed record Ping(string Name, int Value) : IRequest<string>;

    private sealed class PingValidator : AbstractValidator<Ping>
    {
        public PingValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
            RuleFor(x => x.Value).GreaterThan(0).WithMessage("Value must be positive");
        }
    }

    private static ValueTask<string> Next() => new("handled");

    [Fact]
    public async Task NoValidators_CallsNext()
    {
        var behavior = new FluentValidationBehavior<Ping, string>([]);

        var result = await behavior.HandleAsync(new Ping("ok", 1), Next, default);

        result.Should().Be("handled");
    }

    [Fact]
    public async Task AllValid_CallsNext()
    {
        var behavior = new FluentValidationBehavior<Ping, string>([new PingValidator()]);

        var result = await behavior.HandleAsync(new Ping("ok", 1), Next, default);

        result.Should().Be("handled");
    }

    [Fact]
    public async Task Invalid_ThrowsValidationException_WithMappedErrors()
    {
        var behavior = new FluentValidationBehavior<Ping, string>([new PingValidator()]);
        var handlerRan = false;

        Func<Task> act = async () => await behavior.HandleAsync(
            new Ping(string.Empty, 0),
            () => { handlerRan = true; return Next(); },
            default);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().HaveCount(2);
        exception.Which.Errors.Should().Contain(e => e.PropertyName == "Name" && e.ErrorMessage == "Name is required");
        exception.Which.Errors.Should().Contain(e => e.PropertyName == "Value" && e.ErrorMessage == "Value must be positive");
        handlerRan.Should().BeFalse("validation must short-circuit before the handler");
    }

    [Fact]
    public async Task MultipleValidators_AggregatesFailures()
    {
        var second = new InlineValidator<Ping>();
        second.RuleFor(x => x.Name).Must(n => n != "blocked").WithMessage("Name cannot be 'blocked'");

        var behavior = new FluentValidationBehavior<Ping, string>([new PingValidator(), second]);

        Func<Task> act = async () => await behavior.HandleAsync(new Ping("blocked", 0), Next, default);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(e => e.ErrorMessage == "Value must be positive");
        exception.Which.Errors.Should().Contain(e => e.ErrorMessage == "Name cannot be 'blocked'");
    }
}
