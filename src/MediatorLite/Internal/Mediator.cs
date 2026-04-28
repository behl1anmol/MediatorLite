using System.Runtime.CompilerServices;

namespace MediatorLite.Internal;

/// <summary>
/// Internal implementation of the mediator with O(1) source-generated dispatch.
/// Uses compile-time generated dispatch tables for zero-overhead request routing.
/// </summary>
/// <remarks>
/// <para>
/// v2 architecture: all dispatch is handled via pre-compiled delegate dictionaries produced
/// by the MediatorLite source generator. Reflection fallback has been removed — all handlers
/// must be discovered at compile time.
/// </para>
/// <para>
/// Logging and tracing are emitted by the source generator into each generated
/// <c>Pipeline_*</c> and <c>Publish_*</c> method. Opt out at compile time with
/// <c>[assembly: DisableMediatorLogging]</c> / <c>[assembly: DisableMediatorTracing]</c>.
/// </para>
/// </remarks>
internal sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISourceGeneratedMediator _sourceGeneratedMediator;

    public Mediator(
        IServiceProvider serviceProvider,
        ISourceGeneratedMediator sourceGeneratedMediator)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _sourceGeneratedMediator = sourceGeneratedMediator ?? throw new ArgumentNullException(nameof(sourceGeneratedMediator));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var dispatcher = _sourceGeneratedMediator.GetDispatcher(requestType)
            ?? throw new InvalidOperationException(
                $"No handler registered for request type {requestType.FullName}. " +
                $"Ensure a handler implementing IRequestHandler<{requestType.Name}, {typeof(TResponse).Name}> " +
                "is registered and AddGeneratedHandlers() is called.");

        var typedDispatcher = (Func<IServiceProvider, IRequest<TResponse>, CancellationToken, ValueTask<TResponse>>)dispatcher;
        return await typedDispatcher(_serviceProvider, request, cancellationToken).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        var publisher = _sourceGeneratedMediator.GetPublisher(typeof(TNotification));
        return publisher is null
            ? Task.CompletedTask
            : publisher(_serviceProvider, notification, cancellationToken);
    }
}
