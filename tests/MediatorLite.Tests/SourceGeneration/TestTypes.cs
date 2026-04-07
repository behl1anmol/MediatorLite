using MediatorLite.Validation.Models;

namespace MediatorLite.Tests.SourceGeneration;

using System.ComponentModel.DataAnnotations;
using MediatorLite.Validation;
using MediatorValidationResult = MediatorLite.Validation.Models.ValidationResult;

#region Request/Response Types

public record GetUserByIdQuery(int Id) : IRequest<UserDto>;

public record UserDto(int Id, string Name, string Email);

public record CreateUserCommand(string Name, string Email) : IRequest<int>;

public record DeleteUserByIdCommand(int Id) : IRequest;

public record FailingRequest : IRequest<string>;

public record ComputeValueQuery(int Value) : IRequest<int>;

public record DelayedRequest : IRequest<string>;

#endregion

#region Notification Types

public record UserCreatedEvent(int UserId, string Email) : INotification;

[NotificationOptions(
    ExecutionStrategy = NotificationExecutionStrategy.Parallel,
    ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate,
    OverrideGlobal = true)]
public record ParallelEvent(string Message) : INotification;

[NotificationOptions(
    ExecutionStrategy = NotificationExecutionStrategy.StopOnFirst,
    OverrideGlobal = true)]
public record StopOnFirstEvent(string Message) : INotification;

/// <summary>
/// Notification configured for StopOnFirst execution with ContinueAndAggregate error strategy.
/// This enables the "fallback pattern" where if one handler fails, the next is tried.
/// </summary>
[NotificationOptions(
    ExecutionStrategy = NotificationExecutionStrategy.StopOnFirst,
    ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate,
    OverrideGlobal = true)]
public record StopOnFirstFallbackEvent(string Message) : INotification;

/// <summary>
/// Notification configured for StopOnFirst + StopOnFirstError (default error strategy).
/// When the first handler fails, it should throw immediately without trying other handlers.
/// </summary>
[NotificationOptions(
    ExecutionStrategy = NotificationExecutionStrategy.StopOnFirst,
    ErrorStrategy = NotificationErrorStrategy.StopOnFirstError,
    OverrideGlobal = true)]
public record StopOnFirstWithStopOnFirstErrorEvent(string Message) : INotification;

/// <summary>
/// Notification configured for StopOnFirst + ContinueAndAggregate where ALL handlers fail.
/// Should throw AggregateException with all handler failures.
/// </summary>
[NotificationOptions(
    ExecutionStrategy = NotificationExecutionStrategy.StopOnFirst,
    ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate,
    OverrideGlobal = true)]
public record AllFailStopOnFirstWithAggregateEvent(string Message) : INotification;

#endregion

#region Request Handlers

public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDto>
{
    public ValueTask<UserDto> HandleAsync(GetUserByIdQuery request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new UserDto(request.Id, "Test User", "test@example.com"));
    }
}

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, int>
{
    public static int LastCreatedId { get; private set; }
    public static void Reset() => LastCreatedId = 0;

    public ValueTask<int> HandleAsync(CreateUserCommand request, CancellationToken cancellationToken = default)
    {
        LastCreatedId = Random.Shared.Next(1, 1000);
        return ValueTask.FromResult(LastCreatedId);
    }
}

public class DeleteUserByIdCommandHandler : IRequestHandler<DeleteUserByIdCommand>
{
    public static bool WasCalled { get; private set; }
    public static int? LastDeletedId { get; private set; }
    public static void Reset() { WasCalled = false; LastDeletedId = null; }

    public ValueTask HandleAsync(DeleteUserByIdCommand request, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        LastDeletedId = request.Id;
        return ValueTask.CompletedTask;
    }
}

public class FailingRequestHandler : IRequestHandler<FailingRequest, string>
{
    public ValueTask<string> HandleAsync(FailingRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromException<string>(new InvalidOperationException("Handler failed intentionally"));
    }
}

public class ComputeValueQueryHandler : IRequestHandler<ComputeValueQuery, int>
{
    public ValueTask<int> HandleAsync(ComputeValueQuery request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(request.Value * 2);
    }
}

public class DelayedRequestHandler : IRequestHandler<DelayedRequest, string>
{
    public async ValueTask<string> HandleAsync(DelayedRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(1000, cancellationToken);
        return "Done";
    }
}

#endregion

#region Notification Handlers

public class UserCreatedEventHandler1 : INotificationHandler<UserCreatedEvent>
{
    public static List<int> CallOrder { get; } = [];
    public static int CallCount { get; private set; }
    public static void Reset() { CallCount = 0; CallOrder.Clear(); }

    public ValueTask HandleAsync(UserCreatedEvent notification, CancellationToken cancellationToken = default)
    {
        CallCount++;
        CallOrder.Add(1);
        return ValueTask.CompletedTask;
    }
}

[NotificationHandlerOrder(1)]
public class UserCreatedEventHandler2 : INotificationHandler<UserCreatedEvent>
{
    public static int CallCount { get; private set; }
    public static void Reset() => CallCount = 0;

    public ValueTask HandleAsync(UserCreatedEvent notification, CancellationToken cancellationToken = default)
    {
        CallCount++;
        UserCreatedEventHandler1.CallOrder.Add(2);
        return ValueTask.CompletedTask;
    }
}

[NotificationHandlerOrder(2)]
public class UserCreatedEventHandler3 : INotificationHandler<UserCreatedEvent>
{
    public static int CallCount { get; private set; }
    public static void Reset() => CallCount = 0;

    public ValueTask HandleAsync(UserCreatedEvent notification, CancellationToken cancellationToken = default)
    {
        CallCount++;
        UserCreatedEventHandler1.CallOrder.Add(3);
        return ValueTask.CompletedTask;
    }
}

public class ParallelEventFailingHandler : INotificationHandler<ParallelEvent>
{
    public ValueTask HandleAsync(ParallelEvent notification, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromException(new InvalidOperationException("Parallel handler failed"));
    }
}

public class ParallelEventSuccessHandler : INotificationHandler<ParallelEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(ParallelEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.CompletedTask;
    }
}

public class StopOnFirstEventHandler1 : INotificationHandler<StopOnFirstEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(StopOnFirstEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.CompletedTask;
    }
}

[NotificationHandlerOrder(1)]
public class StopOnFirstEventHandler2 : INotificationHandler<StopOnFirstEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(StopOnFirstEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// First handler for fallback event - always fails to test fallback behavior.
/// </summary>
public class StopOnFirstFallbackEventHandler1 : INotificationHandler<StopOnFirstFallbackEvent>
{
    public static bool WasCalled { get; private set; }
    public static Exception? ThrownException { get; private set; }
    public static void Reset() { WasCalled = false; ThrownException = null; }

    public ValueTask HandleAsync(StopOnFirstFallbackEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        ThrownException = new InvalidOperationException("Primary handler failed");
        return ValueTask.FromException(ThrownException);
    }
}

/// <summary>
/// Second handler (fallback) for fallback event - succeeds if reached.
/// </summary>
[NotificationHandlerOrder(1)]
public class StopOnFirstFallbackEventHandler2 : INotificationHandler<StopOnFirstFallbackEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(StopOnFirstFallbackEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Third handler - should never be reached (fallback stops after Handler2 succeeds).
/// </summary>
[NotificationHandlerOrder(2)]
public class StopOnFirstFallbackEventHandler3 : INotificationHandler<StopOnFirstFallbackEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(StopOnFirstFallbackEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Handler that fails for StopOnFirstWithStopOnFirstErrorEvent - tests immediate throw behavior.
/// </summary>
public class StopOnFirstWithStopOnFirstErrorEventFailingHandler : INotificationHandler<StopOnFirstWithStopOnFirstErrorEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(StopOnFirstWithStopOnFirstErrorEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.FromException(new InvalidOperationException("StopOnFirst handler failed"));
    }
}

/// <summary>
/// Success handler for StopOnFirstWithStopOnFirstErrorEvent - should NOT be called when first handler fails.
/// </summary>
[NotificationHandlerOrder(1)]
public class StopOnFirstWithStopOnFirstErrorEventSuccessHandler : INotificationHandler<StopOnFirstWithStopOnFirstErrorEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(StopOnFirstWithStopOnFirstErrorEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// First failing handler for AllFailStopOnFirstWithAggregateEvent - tests aggregate exception.
/// </summary>
public class AllFailStopOnFirstWithAggregateEventHandler1 : INotificationHandler<AllFailStopOnFirstWithAggregateEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(AllFailStopOnFirstWithAggregateEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.FromException(new InvalidOperationException("Handler1 failed"));
    }
}

/// <summary>
/// Second failing handler for AllFailStopOnFirstWithAggregateEvent - tests aggregate exception.
/// </summary>
[NotificationHandlerOrder(1)]
public class AllFailStopOnFirstWithAggregateEventHandler2 : INotificationHandler<AllFailStopOnFirstWithAggregateEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(AllFailStopOnFirstWithAggregateEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.FromException(new InvalidOperationException("Handler2 failed"));
    }
}

#endregion

#region Pipeline Behaviors

[MediatorGeneration(Skip = true)]
public class AddOneBehavior : IPipelineBehavior<ComputeValueQuery, int>
{
    public async ValueTask<int> HandleAsync(
        ComputeValueQuery request,
        RequestHandlerDelegate<int> next,
        CancellationToken cancellationToken = default)
    {
        var result = await next();
        return result + 1;
    }
}

[MediatorGeneration(Skip = true)]
public class MultiplyByTwoBehavior : IPipelineBehavior<ComputeValueQuery, int>
{
    public async ValueTask<int> HandleAsync(
        ComputeValueQuery request,
        RequestHandlerDelegate<int> next,
        CancellationToken cancellationToken = default)
    {
        var result = await next();
        return result * 2;
    }
}

[MediatorGeneration(Skip = true)]
public class GenericLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public static List<string> Calls { get; } = [];
    public static void Reset() => Calls.Clear();

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"Before: {typeof(TRequest).Name}");
        var result = await next();
        Calls.Add($"After: {typeof(TRequest).Name}");
        return result;
    }
}

[MediatorGeneration(Skip = true)]
public class ShortCircuitBehavior : IPipelineBehavior<ComputeValueQuery, int>
{
    public ValueTask<int> HandleAsync(
        ComputeValueQuery request,
        RequestHandlerDelegate<int> next,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(999);
    }
}

#endregion

#region Validation Types

/// <summary>
/// Request with DataAnnotation attributes for testing source-generated validation.
/// Source generator should auto-register DataAnnotationsValidator for this type.
/// </summary>
public sealed record ValidatedCommand : IRequest<string>
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 50 characters")]
    public required string Name { get; init; }

    [Range(1, 100, ErrorMessage = "Value must be between 1 and 100")]
    public int Value { get; init; }
}

/// <summary>
/// Handler for ValidatedCommand - tracks whether it was executed.
/// </summary>
public class ValidatedCommandHandler : IRequestHandler<ValidatedCommand, string>
{
    public static bool WasExecuted { get; set; }
    public static void Reset() => WasExecuted = false;

    public ValueTask<string> HandleAsync(ValidatedCommand request, CancellationToken cancellationToken = default)
    {
        WasExecuted = true;
        return ValueTask.FromResult($"Processed: {request.Name}");
    }
}

/// <summary>
/// Custom validator for ValidatedCommand - discovered by source generator.
/// </summary>
public class ValidatedCommandCustomValidator : IValidator<ValidatedCommand>
{
    public static bool WasExecuted { get; set; }
    public static void Reset() => WasExecuted = false;

    public ValueTask<MediatorValidationResult> ValidateAsync(ValidatedCommand request, CancellationToken cancellationToken = default)
    {
        WasExecuted = true;

        if (request.Name.Contains("blocked"))
        {
            return ValueTask.FromResult(MediatorValidationResult.Failure(
                new ValidationError("Name", "Name cannot contain 'blocked'")));
        }

        return ValueTask.FromResult(MediatorValidationResult.Success);
    }
}

/// <summary>
/// Behavior for tracking execution order in tests.
/// Marked as Skip so it's not auto-registered - each test controls its own registration.
/// </summary>
[MediatorGeneration(Skip = true)]
public class ExecutionOrderTrackingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public static List<string> ExecutionLog { get; } = [];
    public static void Reset() => ExecutionLog.Clear();

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        ExecutionLog.Add($"TrackingBehavior:Before:{typeof(TRequest).Name}");
        var result = await next();
        ExecutionLog.Add($"TrackingBehavior:After:{typeof(TRequest).Name}");
        return result;
    }
}

#endregion
