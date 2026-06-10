namespace MediatorLite;

/// <summary>
/// Defines how notification handlers should be executed.
/// </summary>
public enum NotificationExecutionStrategy
{
    /// <summary>
    /// Execute handlers one after another in order.
    /// This is the default strategy.
    /// </summary>
    Sequential = 0,

    /// <summary>
    /// Execute all handlers concurrently using Task.WhenAll.
    /// </summary>
    Parallel = 1,

    /// <summary>
    /// Stop execution after the first handler completes successfully.
    /// Useful for "first handler wins" or fallback scenarios.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Handlers are attempted in order (respecting <see cref="NotificationHandlerOrderAttribute"/>).
    /// Execution stops as soon as one handler succeeds without throwing.
    /// </para>
    /// <para>
    /// Behavior depends on the <see cref="NotificationErrorStrategy"/>:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="NotificationErrorStrategy.StopOnFirstError"/>: The first handler failure
    /// throws immediately; no fallback to subsequent handlers occurs.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NotificationErrorStrategy.ContinueAndAggregate"/>: On failure, the next
    /// handler is tried. If all handlers fail, an <see cref="AggregateException"/> containing
    /// all collected exceptions is thrown.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    StopOnFirst = 2
}

/// <summary>
/// Defines how errors should be handled during notification publishing.
/// </summary>
public enum NotificationErrorStrategy
{
    /// <summary>
    /// Stop execution immediately when a handler throws an exception.
    /// The exception is propagated to the caller.
    /// </summary>
    /// <remarks>
    /// When combined with <see cref="NotificationExecutionStrategy.StopOnFirst"/>,
    /// the first handler failure throws immediately with no fallback to subsequent handlers.
    /// </remarks>
    StopOnFirstError = 0,

    /// <summary>
    /// Continue executing all handlers even if some throw exceptions.
    /// All exceptions are collected and thrown as an <see cref="AggregateException"/>.
    /// </summary>
    /// <remarks>
    /// When combined with <see cref="NotificationExecutionStrategy.StopOnFirst"/>,
    /// a failing handler does not stop execution; the next handler is attempted.
    /// If all handlers fail, an <see cref="AggregateException"/> with all collected
    /// exceptions is thrown. If at least one handler succeeds, no exception is thrown.
    /// </remarks>
    ContinueAndAggregate = 1
}

/// <summary>
/// Specifies the execution order of a notification handler.
/// </summary>
/// <remarks>
/// Lower values execute first. Handlers without this attribute default to order 0.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotificationHandlerOrderAttribute(int order) : Attribute
{
    /// <summary>
    /// Gets the execution order. Lower values execute first.
    /// </summary>
    public int Order { get; } = order;
}

/// <summary>
/// Specifies the notification execution strategy for a specific notification type at compile time.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute to a notification class to set its execution strategy. Presence of the
/// attribute means the strategy is explicitly set and will override any assembly-level default
/// declared via <see cref="DefaultNotificationExecutionAttribute"/>. Absence of the attribute
/// means the strategy falls back to the assembly default (if any) or the library default
/// (<see cref="NotificationExecutionStrategy.Sequential"/>).
/// </para>
/// <para>
/// This attribute is consumed at compile time by the MediatorLite source generator; it has no
/// runtime effect.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotificationExecutionAttribute(NotificationExecutionStrategy strategy) : Attribute
{
    /// <summary>
    /// Gets the execution strategy for this notification type.
    /// </summary>
    public NotificationExecutionStrategy Strategy { get; } = strategy;
}

/// <summary>
/// Specifies the notification error handling strategy for a specific notification type at compile time.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute to a notification class to set its error handling strategy. Presence of
/// the attribute means the strategy is explicitly set and will override any assembly-level default
/// declared via <see cref="DefaultNotificationErrorAttribute"/>. Absence of the attribute means
/// the strategy falls back to the assembly default (if any) or the library default
/// (<see cref="NotificationErrorStrategy.StopOnFirstError"/>).
/// </para>
/// <para>
/// This attribute is consumed at compile time by the MediatorLite source generator; it has no
/// runtime effect.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotificationErrorAttribute(NotificationErrorStrategy strategy) : Attribute
{
    /// <summary>
    /// Gets the error handling strategy for this notification type.
    /// </summary>
    public NotificationErrorStrategy Strategy { get; } = strategy;
}

/// <summary>
/// Specifies the assembly-wide default notification execution strategy at compile time.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute at the assembly level to set the default execution strategy for all
/// notification types in the assembly. Per-notification <see cref="NotificationExecutionAttribute"/>
/// takes precedence when present. Absence of this attribute means notifications without an explicit
/// <see cref="NotificationExecutionAttribute"/> fall back to the library default
/// (<see cref="NotificationExecutionStrategy.Sequential"/>).
/// </para>
/// <para>
/// This attribute is consumed at compile time by the MediatorLite source generator; it has no
/// runtime effect.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DefaultNotificationExecutionAttribute(NotificationExecutionStrategy strategy) : Attribute
{
    /// <summary>
    /// Gets the assembly-wide default execution strategy.
    /// </summary>
    public NotificationExecutionStrategy Strategy { get; } = strategy;
}

/// <summary>
/// Specifies the assembly-wide default notification error handling strategy at compile time.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute at the assembly level to set the default error handling strategy for all
/// notification types in the assembly. Per-notification <see cref="NotificationErrorAttribute"/>
/// takes precedence when present. Absence of this attribute means notifications without an explicit
/// <see cref="NotificationErrorAttribute"/> fall back to the library default
/// (<see cref="NotificationErrorStrategy.StopOnFirstError"/>).
/// </para>
/// <para>
/// This attribute is consumed at compile time by the MediatorLite source generator; it has no
/// runtime effect.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DefaultNotificationErrorAttribute(NotificationErrorStrategy strategy) : Attribute
{
    /// <summary>
    /// Gets the assembly-wide default error handling strategy.
    /// </summary>
    public NotificationErrorStrategy Strategy { get; } = strategy;
}

/// <summary>
/// Specifies the execution order of a pipeline behavior.
/// </summary>
/// <remarks>
/// Lower values execute first (outer behaviors wrap inner behaviors).
/// Behaviors without this attribute default to order 0.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BehaviorOrderAttribute(int order) : Attribute
{
    /// <summary>
    /// Gets the execution order. Lower values execute first.
    /// </summary>
    public int Order { get; } = order;
}

/// <summary>
/// Controls source generation behavior for a specific type.
/// </summary>
/// <remarks>
/// Use this attribute to skip automatic source generation for specific handlers
/// when you need custom registration logic.
/// </remarks>
[Obsolete("This attribute is no longer valid with the complete source generator implementation.")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MediatorGenerationAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether to skip source generation for this type.
    /// Default is false.
    /// </summary>
    public bool Skip { get; set; }
}

/// <summary>
/// Disables MediatorLite's built-in logging for the entire assembly at compile time.
/// </summary>
/// <remarks>
/// <para>
/// When this attribute is present, the MediatorLite source generator emits no logging calls
/// in the generated request dispatchers or notification publishers. The default (attribute absent)
/// is that built-in logging is enabled and emits <c>LogDebug</c> entries at the boundaries of
/// <see cref="IMediator.SendAsync{TResponse}"/> and <see cref="IMediator.PublishAsync{TNotification}"/>.
/// </para>
/// <para>
/// Log level is controlled via <c>Microsoft.Extensions.Logging</c> filter configuration (e.g.
/// <c>appsettings.json</c> or <c>AddFilter</c>), not by this attribute.
/// </para>
/// <para>
/// This attribute is consumed at compile time by the MediatorLite source generator; it has no
/// runtime effect.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DisableMediatorLoggingAttribute : Attribute { }

/// <summary>
/// Disables MediatorLite's built-in OpenTelemetry tracing for the entire assembly at compile time.
/// </summary>
/// <remarks>
/// <para>
/// When this attribute is present, the MediatorLite source generator emits no <see cref="System.Diagnostics.ActivitySource"/>
/// calls in the generated request dispatchers or notification publishers. The default (attribute absent)
/// is that tracing is enabled.
/// </para>
/// <para>
/// To toggle tracing at runtime, use an <see cref="System.Diagnostics.ActivityListener"/> or the
/// OpenTelemetry SDK instead — no listener means activity creation is near-zero cost anyway.
/// </para>
/// <para>
/// This attribute is consumed at compile time by the MediatorLite source generator; it has no
/// runtime effect.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DisableMediatorTracingAttribute : Attribute { }
