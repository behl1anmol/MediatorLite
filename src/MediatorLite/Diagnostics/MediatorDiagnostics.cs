using System.Diagnostics;

namespace MediatorLite.Diagnostics;

/// <summary>
/// Provides OpenTelemetry ActivitySource for distributed tracing in MediatorLite.
/// </summary>
public static class MediatorActivitySource
{
    /// <summary>
    /// The name of the activity source.
    /// </summary>
    public const string SourceName = "MediatorLite";

    /// <summary>
    /// The version of the activity source.
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// The ActivitySource for MediatorLite tracing.
    /// </summary>
    public static readonly ActivitySource Source = new(SourceName, Version);

    /// <summary>
    /// Activity names for different operations.
    /// </summary>
    public static class ActivityNames
    {
        /// <summary>Send request activity name prefix.</summary>
        public const string SendRequest = "MediatorLite.Send";

        /// <summary>Publish notification activity name prefix.</summary>
        public const string PublishNotification = "MediatorLite.Publish";

        /// <summary>Pipeline behavior activity name prefix.</summary>
        public const string PipelineBehavior = "MediatorLite.Behavior";

        /// <summary>Notification handler activity name prefix.</summary>
        public const string NotificationHandler = "MediatorLite.NotificationHandler";
    }

    /// <summary>
    /// Tag names for activity attributes.
    /// </summary>
    public static class Tags
    {
        /// <summary>Request type tag.</summary>
        public const string RequestType = "mediatorlite.request.type";

        /// <summary>Response type tag.</summary>
        public const string ResponseType = "mediatorlite.response.type";

        /// <summary>Notification type tag.</summary>
        public const string NotificationType = "mediatorlite.notification.type";

        /// <summary>Handler type tag.</summary>
        public const string HandlerType = "mediatorlite.handler.type";

        /// <summary>Behavior type tag.</summary>
        public const string BehaviorType = "mediatorlite.behavior.type";

        /// <summary>Handler count tag.</summary>
        public const string HandlerCount = "mediatorlite.handler.count";

        /// <summary>Execution strategy tag.</summary>
        public const string ExecutionStrategy = "mediatorlite.execution.strategy";

        /// <summary>Error tag.</summary>
        public const string Error = "error";

        /// <summary>Error message tag.</summary>
        public const string ErrorMessage = "error.message";
    }
}

/// <summary>
/// Diagnostic events for MediatorLite operations.
/// </summary>
public static class MediatorDiagnostics
{
    /// <summary>
    /// DiagnosticListener for MediatorLite events.
    /// </summary>
    public static readonly DiagnosticListener Listener = new("MediatorLite");

    /// <summary>
    /// Event name constants.
    /// </summary>
    public static class Events
    {
        /// <summary>Request started event.</summary>
        public const string RequestStarted = "MediatorLite.RequestStarted";

        /// <summary>Request completed event.</summary>
        public const string RequestCompleted = "MediatorLite.RequestCompleted";

        /// <summary>Request failed event.</summary>
        public const string RequestFailed = "MediatorLite.RequestFailed";

        /// <summary>Notification published event.</summary>
        public const string NotificationPublished = "MediatorLite.NotificationPublished";

        /// <summary>Notification handler started event.</summary>
        public const string NotificationHandlerStarted = "MediatorLite.NotificationHandlerStarted";

        /// <summary>Notification handler completed event.</summary>
        public const string NotificationHandlerCompleted = "MediatorLite.NotificationHandlerCompleted";
    }
}
