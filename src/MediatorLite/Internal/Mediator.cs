using MediatorLite.Configuration;
using MediatorLite.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace MediatorLite.Internal;

/// <summary>
/// Internal implementation of the mediator with O(1) source-generated dispatch.
/// Uses compile-time generated dispatch tables for zero-overhead request routing.
/// </summary>
/// <remarks>
/// v2 Architecture: All dispatch is handled via pre-compiled delegate dictionaries.
/// Reflection fallback has been removed — all handlers must be discovered at compile-time.
/// </remarks>
internal sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Mediator> _logger;
    private readonly MediatorOptions _options;
    private readonly ISourceGeneratedMediator _sourceGeneratedMediator;

    public Mediator(
        IServiceProvider serviceProvider,
        ILogger<Mediator> logger,
        MediatorOptions options,
        ISourceGeneratedMediator sourceGeneratedMediator)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sourceGeneratedMediator = sourceGeneratedMediator ?? throw new ArgumentNullException(nameof(sourceGeneratedMediator));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();

        // O(1) dispatch lookup
        var dispatcher = _sourceGeneratedMediator.GetDispatcher(requestType);
        
        if (dispatcher is null)
        {
            throw new InvalidOperationException(
                $"No handler registered for request type {requestType.FullName}. " +
                $"Ensure a handler implementing IRequestHandler<{requestType.Name}, {typeof(TResponse).Name}> " +
                "is registered and AddGeneratedHandlers() is called.");
        }

        // Start tracing if enabled
        using var activity = _options.EnableTracing
            ? MediatorActivitySource.Source.StartActivity(
                $"{MediatorActivitySource.ActivityNames.SendRequest} {requestType.Name}",
                ActivityKind.Internal)
            : null;

        activity?.SetTag(MediatorActivitySource.Tags.RequestType, requestType.FullName);
        activity?.SetTag(MediatorActivitySource.Tags.ResponseType, typeof(TResponse).FullName);

        if (_options.EnableBuiltInLogging)
        {
            _logger.Log(_options.DefaultLogLevel, "Sending request {RequestType}", requestType.Name);
        }

        try
        {
            // Execute the pre-compiled pipeline
            var result = await dispatcher(_serviceProvider, request, cancellationToken).ConfigureAwait(false);

            if (_options.EnableBuiltInLogging)
            {
                _logger.Log(_options.DefaultLogLevel, "Request {RequestType} handled successfully", requestType.Name);
            }

            return (TResponse)result;
        }
        catch (Exception ex)
        {
            activity?.SetTag(MediatorActivitySource.Tags.Error, true);
            activity?.SetTag(MediatorActivitySource.Tags.ErrorMessage, ex.Message);

            if (_options.EnableBuiltInLogging)
            {
                _logger.LogError(ex, "Error handling request {RequestType}", requestType.Name);
            }

            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        var notificationType = typeof(TNotification);

        // O(1) publisher lookup
        var publisher = _sourceGeneratedMediator.GetPublisher(notificationType);
        
        if (publisher is null)
        {
            // No handlers registered — this is valid, just a no-op
            if (_options.EnableBuiltInLogging)
            {
                _logger.Log(_options.DefaultLogLevel,
                    "No handlers registered for notification {NotificationType}", notificationType.Name);
            }
            return;
        }

        // Start tracing if enabled
        using var activity = _options.EnableTracing
            ? MediatorActivitySource.Source.StartActivity(
                $"{MediatorActivitySource.ActivityNames.PublishNotification} {notificationType.Name}",
                ActivityKind.Internal)
            : null;

        activity?.SetTag(MediatorActivitySource.Tags.NotificationType, notificationType.FullName);

        if (_options.EnableBuiltInLogging)
        {
            _logger.Log(_options.DefaultLogLevel, "Publishing notification {NotificationType}", notificationType.Name);
        }

        try
        {
            // Execute the pre-compiled notification pipeline
            await publisher(_serviceProvider, notification, cancellationToken).ConfigureAwait(false);

            if (_options.EnableBuiltInLogging)
            {
                _logger.Log(_options.DefaultLogLevel,
                    "Notification {NotificationType} published successfully", notificationType.Name);
            }
        }
        catch (Exception ex)
        {
            activity?.SetTag(MediatorActivitySource.Tags.Error, true);
            activity?.SetTag(MediatorActivitySource.Tags.ErrorMessage, ex.Message);

            if (_options.EnableBuiltInLogging)
            {
                _logger.LogError(ex, "Error publishing notification {NotificationType}", notificationType.Name);
            }

            throw;
        }
    }
}
