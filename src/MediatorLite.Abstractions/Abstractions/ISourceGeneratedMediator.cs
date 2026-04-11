using System.Runtime.CompilerServices;

namespace MediatorLite;

/// <summary>
/// Delegate for dispatching a request through its pipeline (behaviors + handler).
/// </summary>
/// <param name="serviceProvider">The service provider for resolving dependencies.</param>
/// <param name="request">The request object (will be cast to concrete type).</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>A task containing the boxed response.</returns>
/// <remarks>
/// <para>
/// <b>Boxing tradeoff:</b> This delegate returns <c>Task&lt;object&gt;</c> which causes boxing
/// for value type responses (e.g., <c>int</c>, <c>bool</c>, <c>Guid</c>, custom structs).
/// Each value type response incurs a heap allocation when boxed to <c>object</c>.
/// </para>
/// <para>
/// This is a deliberate design tradeoff for compile-time simplicity: a single delegate signature
/// allows the source generator to produce a unified dispatch table (<c>Dictionary&lt;Type, RequestDispatcher&gt;</c>)
/// without requiring generic delegate instantiation per request type. The boxing cost is typically
/// negligible compared to I/O-bound handler work, but may be measurable in high-throughput,
/// CPU-bound scenarios with value type responses.
/// </para>
/// </remarks>
public delegate Task<object> RequestDispatcher(
    IServiceProvider serviceProvider,
    object request,
    CancellationToken cancellationToken);

/// <summary>
/// Delegate for publishing a notification to all handlers.
/// </summary>
/// <param name="serviceProvider">The service provider for resolving handlers.</param>
/// <param name="notification">The notification object (will be cast to concrete type).</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>A task representing the publish operation.</returns>
/// <remarks>
/// Unlike <see cref="RequestDispatcher"/>, this delegate returns <c>Task</c> (non-generic),
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
/// <para>
/// The generated implementation uses <c>Dictionary&lt;Type, RequestDispatcher&gt;</c> for O(1) lookup
/// instead of switch expressions with pattern matching.
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
    /// A <see cref="RequestDispatcher"/> delegate if the request type was discovered at compile-time;
    /// otherwise, null.
    /// </returns>
    /// <remarks>
    /// The returned delegate is a static method reference with no per-call allocation.
    /// The pipeline is fully unrolled at compile-time based on discovered behaviors.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    RequestDispatcher? GetDispatcher(Type requestType);

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

    /// <summary>
    /// Gets the notification options for a notification type.
    /// </summary>
    /// <param name="notificationType">The notification type to get options for.</param>
    /// <returns>
    /// A tuple of (ExecutionStrategy, ErrorStrategy) if the notification has options configured
    /// via <see cref="NotificationOptionsAttribute"/>; otherwise, null indicating default options should be used.
    /// </returns>
    (NotificationExecutionStrategy ExecutionStrategy, NotificationErrorStrategy ErrorStrategy)? GetNotificationOptions(Type notificationType);
}
