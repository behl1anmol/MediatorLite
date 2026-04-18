namespace MediatorLite.Internal;

/// <summary>
/// Null implementation of <see cref="ISourceGeneratedMediator"/> that returns null for all lookups.
/// Used as a fallback when no source-generated mediator is registered.
/// All dispatch attempts through this implementation will fail with appropriate error messages.
/// </summary>
internal sealed class NullSourceGeneratedMediator : ISourceGeneratedMediator
{
    public static readonly NullSourceGeneratedMediator Instance = new();

    private NullSourceGeneratedMediator() { }

    public RequestDispatcher? GetDispatcher(Type requestType) => null;

    public NotificationPublisher? GetPublisher(Type notificationType) => null;
}
