using System.Runtime.CompilerServices;

namespace MediatorLite;

/// <summary>
/// Delegate for publishing a notification to all handlers.
/// </summary>
/// <param name="serviceProvider">The service provider for resolving handlers.</param>
/// <param name="notification">The notification object (will be cast to concrete type).</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>A task representing the publish operation.</returns>
/// <remarks>
/// This delegate returns <c>Task</c> (non-generic),
/// so notifications do not incur boxing overhead for responses. The notification object itself
/// is passed as <c>object</c> and cast to the concrete type in the generated dispatch method.
/// </remarks>
public delegate Task NotificationPublisher(
    IServiceProvider serviceProvider,
    object notification,
    CancellationToken cancellationToken);

/// <summary>
/// Interface for source-generated mediator dispatch functionality.
/// Implementations are generated at compile-time with O(1) dispatch via static dictionaries.
/// </summary>
/// <remarks>
/// <para>
/// v2 Architecture: This interface provides O(1) dispatch through pre-compiled delegate tables.
/// Each request type has a fully-typed, unrolled pipeline method generated at compile-time,
/// eliminating pattern matching, delegate chain construction, and behavior resolution overhead.
/// </para>
/// </remarks>
public interface ISourceGeneratedMediator
{
    /// <summary>
    /// Gets the dispatch delegate for a request type.
    /// The delegate executes the full pipeline (behaviors + handler) with compile-time typed code.
    /// </summary>
    /// <param name="requestType">The concrete request type.</param>
    /// <returns>
    /// A <see cref="Delegate"/> of type <c>Func&lt;IServiceProvider, IRequest&lt;TResponse&gt;, CancellationToken, ValueTask&lt;TResponse&gt;&gt;</c>
    /// if the request type was discovered at compile-time; otherwise, null.
    /// </returns>
    /// <remarks>
    /// The returned delegate is a static method reference with no per-call allocation.
    /// The pipeline is fully unrolled at compile-time based on discovered behaviors.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Delegate? GetDispatcher(Type requestType);

    /// <summary>
    /// Gets the publish delegate for a notification type.
    /// The delegate executes all handlers with the configured execution strategy.
    /// </summary>
    /// <param name="notificationType">The concrete notification type.</param>
    /// <returns>
    /// A <see cref="NotificationPublisher"/> delegate if the notification type was discovered at compile-time;
    /// otherwise, null.
    /// </returns>
    /// <remarks>
    /// Handlers are pre-sorted by <see cref="NotificationHandlerOrderAttribute"/> at compile-time.
    /// Execution strategy (sequential/parallel/stop-on-first) is baked into the generated method.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    NotificationPublisher? GetPublisher(Type notificationType);
}
