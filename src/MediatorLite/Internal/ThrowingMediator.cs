namespace MediatorLite.Internal;

/// <summary>
/// Fallback <see cref="IMediator"/> registered by <c>AddMediatorLite()</c> when no
/// source-generated mediator is present. Every dispatch attempt throws with guidance,
/// surfacing missing source generation as a clear runtime diagnostic instead of a
/// silent service-resolution failure.
/// </summary>
internal sealed class ThrowingMediator : IMediator
{
    private const string Message =
        "No source-generated mediator is registered. Reference the MediatorLite.SourceGeneration " +
        "analyzer package from the assembly that contains your handlers and call " +
        "services.AddGeneratedHandlers() so the generated mediator replaces this fallback.";

    public ValueTask<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(Message);

    public ValueTask PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
        => throw new InvalidOperationException(Message);
}
