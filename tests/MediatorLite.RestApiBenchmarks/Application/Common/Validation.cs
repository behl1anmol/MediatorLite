namespace MediatorLite.RestApiBenchmarks.Application.Common;

public interface IAppValidator<in TRequest>
{
    ValueTask<IReadOnlyList<string>> ValidateAsync(TRequest request, CancellationToken cancellationToken);
}

public sealed class AppValidationException : Exception
{
    public AppValidationException(IReadOnlyList<string> errors)
        : base("Request validation failed.")
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

public sealed class OrderConcurrencyException : Exception
{
    public OrderConcurrencyException(string message)
        : base(message)
    {
    }
}
