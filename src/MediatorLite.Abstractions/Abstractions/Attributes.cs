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
/// Specifies notification-specific execution options that override global settings.
/// </summary>
/// <remarks>
/// Apply this attribute to a notification class to override the global
/// <see cref="NotificationExecutionStrategy"/> and <see cref="NotificationErrorStrategy"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotificationOptionsAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the execution strategy for this notification type.
    /// If not set, the global strategy from MediatorOptions is used.
    /// </summary>
    public NotificationExecutionStrategy ExecutionStrategy { get; set; } = NotificationExecutionStrategy.Sequential;

    /// <summary>
    /// Gets or sets the error handling strategy for this notification type.
    /// If not set, the global strategy from MediatorOptions is used.
    /// </summary>
    public NotificationErrorStrategy ErrorStrategy { get; set; } = NotificationErrorStrategy.ContinueAndAggregate;

    /// <summary>
    /// Gets or sets whether this attribute's values should override the global settings.
    /// Default is true.
    /// </summary>
    public bool OverrideGlobal { get; set; } = true;
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
/// Controls logging behavior for a specific request type.
/// </summary>
/// <remarks>
/// Use this attribute to override global logging settings for specific requests.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MediatorLoggingAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether logging is enabled for this request type.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include the request payload in logs.
    /// Default is false for security reasons.
    /// </summary>
    public bool IncludePayload { get; set; }

    /// <summary>
    /// Gets or sets the log level for this request type.
    /// Uses Microsoft.Extensions.Logging.LogLevel values as integers.
    /// Default is Information (2).
    /// </summary>
    public int LogLevel { get; set; } = 2; // LogLevel.Information
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
