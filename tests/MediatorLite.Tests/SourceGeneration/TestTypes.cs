using FluentValidation;

namespace MediatorLite.Tests.SourceGeneration;

#region Request/Response Types

public record GetUserByIdQuery(int Id) : IRequest<UserDto>;

public record UserDto(int Id, string Name, string Email);

public record CreateUserCommand(string Name, string Email) : IRequest<int>;

public record DeleteUserByIdCommand(int Id) : IRequest;

public record FailingRequest : IRequest<string>;

public record ComputeValueQuery(int Value) : IRequest<int>;

public record DelayedRequest : IRequest<string>;

public record ShortCircuitQuery : IRequest;

#endregion

#region Notification Types

public record UserCreatedEvent(int UserId, string Email) : INotification;

[NotificationExecution(NotificationExecutionStrategy.Parallel)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record ParallelEvent(string Message) : INotification;

[NotificationExecution(NotificationExecutionStrategy.StopOnFirst)]
public record StopOnFirstEvent(string Message) : INotification;

/// <summary>
/// Notification configured for StopOnFirst execution with ContinueAndAggregate error strategy.
/// This enables the "fallback pattern" where if one handler fails, the next is tried.
/// </summary>
[NotificationExecution(NotificationExecutionStrategy.StopOnFirst)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record StopOnFirstFallbackEvent(string Message) : INotification;

/// <summary>
/// Notification configured for StopOnFirst + StopOnFirstError (default error strategy).
/// When the first handler fails, it should throw immediately without trying other handlers.
/// </summary>
[NotificationExecution(NotificationExecutionStrategy.StopOnFirst)]
[NotificationError(NotificationErrorStrategy.StopOnFirstError)]
public record StopOnFirstWithStopOnFirstErrorEvent(string Message) : INotification;

/// <summary>
/// Notification configured for StopOnFirst + ContinueAndAggregate where ALL handlers fail.
/// Should throw AggregateException with all handler failures.
/// </summary>
[NotificationExecution(NotificationExecutionStrategy.StopOnFirst)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record AllFailStopOnFirstWithAggregateEvent(string Message) : INotification;

#endregion

#region Request Handlers

public class ShortCircuitCommandHandler : IRequestHandler<ShortCircuitQuery>
{
    public ValueTask HandleAsync(ShortCircuitQuery request, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}

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

[BehaviorOrder(1)]
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

[BehaviorOrder(2)]
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

[BehaviorOrder(1)]
public class ShortCircuitBehavior : IPipelineBehavior<ShortCircuitQuery, Unit>
{
    public static bool Executed = false;
    public static void Reset() => Executed = false;
    public ValueTask<Unit> HandleAsync(
        ShortCircuitQuery request,
        RequestHandlerDelegate<Unit> next,
        CancellationToken cancellationToken = default)
    {
        Executed = true;
        return Unit.CompletedTask;
    }
}

[BehaviorOrder(2)]
public class ShortCircuitLoggerBehavior : IPipelineBehavior<ShortCircuitQuery, Unit>
{
    public static bool Executed = false;
    public static void Reset() => Executed = false;
    public ValueTask<Unit> HandleAsync(
        ShortCircuitQuery request,
        RequestHandlerDelegate<Unit> next,
        CancellationToken cancellationToken = default)
    {
        Executed = true;
        return Unit.CompletedTask;
    }
}

#endregion

#region Validation Types

/// <summary>
/// Request validated by a FluentValidation validator and discovered by the source generator.
/// </summary>
public sealed record ValidatedCommand : IRequest<string>
{
    public required string Name { get; init; }

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
/// FluentValidation validator for ValidatedCommand - discovered by the source generator
/// and run as the outermost pipeline behavior. Tracks whether it executed.
/// </summary>
public class ValidatedCommandCustomValidator : global::FluentValidation.AbstractValidator<ValidatedCommand>
{
    public static bool WasExecuted { get; set; }
    public static void Reset() => WasExecuted = false;

    public ValidatedCommandCustomValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .Length(2, 50).WithMessage("Name must be between 2 and 50 characters")
            .Must(name => name is null || !name.Contains("blocked"))
                .WithMessage("Name cannot contain 'blocked'");

        RuleFor(x => x.Value)
            .InclusiveBetween(1, 100).WithMessage("Value must be between 1 and 100");
    }

    public override global::System.Threading.Tasks.Task<global::FluentValidation.Results.ValidationResult> ValidateAsync(
        global::FluentValidation.ValidationContext<ValidatedCommand> context,
        CancellationToken cancellation = default)
    {
        WasExecuted = true;
        return base.ValidateAsync(context, cancellation);
    }
}

/// <summary>
/// Behavior for tracking execution order in tests.
/// </summary>
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

#region Parallel sync-throw fixtures

[NotificationExecution(NotificationExecutionStrategy.Parallel)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record ParallelSyncThrowEvent(string Message) : INotification;

public class ParallelSyncThrowHandler1 : INotificationHandler<ParallelSyncThrowEvent>
{
    public ValueTask HandleAsync(ParallelSyncThrowEvent notification, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("sync-throw-handler-1");
    }
}

public class ParallelSyncThrowHandler2 : INotificationHandler<ParallelSyncThrowEvent>
{
    public ValueTask HandleAsync(ParallelSyncThrowEvent notification, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("sync-throw-handler-2");
    }
}

#endregion

#region Parallel start/await phase probe fixtures

/// <summary>
/// Notification used to observe the two phases of parallel publishing: the synchronous
/// "start phase" (every handler invoked before any is awaited) and the "await phase"
/// (every started <see cref="ValueTask"/> awaited to completion). Both handlers suspend
/// on a shared gate so a test can inspect state exactly at the phase boundary.
/// </summary>
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
public record ParallelPhaseEvent(string Message) : INotification;

/// <summary>
/// Shared probe state for <see cref="ParallelPhaseEvent"/> handlers. The gate is completed
/// inline by the test thread, so handler continuations resume single-threaded — the lists
/// need no synchronization.
/// </summary>
public static class ParallelPhaseProbe
{
    /// <summary>Records each handler's synchronous prefix — the start phase.</summary>
    public static List<string> StartLog { get; private set; } = [];

    /// <summary>Records each handler's post-await continuation — the await phase.</summary>
    public static List<string> EndLog { get; private set; } = [];

    /// <summary>Gate that every handler suspends on until the test releases it.</summary>
    public static TaskCompletionSource Gate { get; private set; } = new();

    public static void Reset()
    {
        StartLog = [];
        EndLog = [];
        Gate = new TaskCompletionSource();
    }

    /// <summary>Releases the gate, unblocking every started handler's await phase.</summary>
    public static void Release() => Gate.TrySetResult();
}

public sealed class ParallelPhaseHandler1 : INotificationHandler<ParallelPhaseEvent>
{
    public async ValueTask HandleAsync(ParallelPhaseEvent notification, CancellationToken cancellationToken = default)
    {
        ParallelPhaseProbe.StartLog.Add("h1");                    // start phase: runs before any await
        await ParallelPhaseProbe.Gate.Task.ConfigureAwait(false); // suspend → lets the next handler start
        ParallelPhaseProbe.EndLog.Add("h1");                      // await phase: continuation after release
    }
}

[NotificationHandlerOrder(1)]
public sealed class ParallelPhaseHandler2 : INotificationHandler<ParallelPhaseEvent>
{
    public async ValueTask HandleAsync(ParallelPhaseEvent notification, CancellationToken cancellationToken = default)
    {
        ParallelPhaseProbe.StartLog.Add("h2");
        await ParallelPhaseProbe.Gate.Task.ConfigureAwait(false);
        ParallelPhaseProbe.EndLog.Add("h2");
    }
}

#endregion

#region Bug-Hunt Regression Fixtures

// ── F9: genuine zero-handler notification ────────────────────────────────────────────
// No INotificationHandler<OrphanEvent> exists anywhere in this assembly; publishing it
// must hit the generated `default: return default;` arm and complete silently.

public record OrphanEvent(string Message) : INotification;

// ── F2: record handler discovery ─────────────────────────────────────────────────────
// Handlers declared as records compile like classes but are a different syntax node;
// the generator used to silently skip them.

public record RecordHandledQuery(int Value) : IRequest<int>;

public sealed record RecordDeclaredQueryHandler : IRequestHandler<RecordHandledQuery, int>
{
    public ValueTask<int> HandleAsync(RecordHandledQuery request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(request.Value + 100);
    }
}

// ── F1: partial handler declared across two parts, each with a base list ─────────────
// The generator's syntax provider visits every declaration node; without deduplication
// the handler was registered — and invoked — once per part.

public interface IPartialHandlerMarker;

public partial class PartialDeclaredEventHandler : INotificationHandler<PartialHandledEvent>
{
    public static int CallCount { get; private set; }
    public static void Reset() => CallCount = 0;

    public ValueTask HandleAsync(PartialHandledEvent notification, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return ValueTask.CompletedTask;
    }
}

public partial class PartialDeclaredEventHandler : IPartialHandlerMarker;

public record PartialHandledEvent(string Message) : INotification;

// ── F3: one request type dispatched with two distinct response types ─────────────────
// MultiResponseQuery is handled both as IRequest<int> and as IRequest<string>; each
// response type must reach its own handler through the shared switch arm.

public record MultiResponseQuery(int Value) : IRequest<int>, IRequest<string>;

public sealed class MultiResponseIntHandler : IRequestHandler<MultiResponseQuery, int>
{
    public ValueTask<int> HandleAsync(MultiResponseQuery request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(request.Value * 10);
    }
}

public sealed class MultiResponseStringHandler : IRequestHandler<MultiResponseQuery, string>
{
    public ValueTask<string> HandleAsync(MultiResponseQuery request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult($"value:{request.Value}");
    }
}

// ── PR #22 P2: multi-response request whose response type name has non-identifier chars ──
// int[] renders as "int[]"; the response suffix reaches the generated Send_* method name,
// so unsanitized brackets would emit an invalid identifier and fail this project's build.
// The fixture existing and compiling is the regression guard.

public record ArrayItemsQuery(int Count) : IRequest<int[]>, IRequest<string>;

public sealed class ArrayItemsIntArrayHandler : IRequestHandler<ArrayItemsQuery, int[]>
{
    public ValueTask<int[]> HandleAsync(ArrayItemsQuery request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Enumerable.Range(0, request.Count).ToArray());
    }
}

public sealed class ArrayItemsStringHandler : IRequestHandler<ArrayItemsQuery, string>
{
    public ValueTask<string> HandleAsync(ArrayItemsQuery request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult($"count:{request.Count}");
    }
}

#endregion

#region Handler-Internal Cancellation Fixtures (F8)

// ContinueAndAggregate treats a handler's own OperationCanceledException as an ordinary
// fault when the publish CancellationToken is NOT cancelled: every fault is aggregated
// (parallel) and remaining handlers still run (sequential). Only genuine cancellation of
// the publish token surfaces an OperationCanceledException unwrapped.
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record ParallelCancellingEvent(string Message) : INotification;

public sealed class ParallelCancellingHandler : INotificationHandler<ParallelCancellingEvent>
{
    public ValueTask HandleAsync(ParallelCancellingEvent notification, CancellationToken cancellationToken = default)
    {
        // Unrelated token: the publish CancellationToken itself is NOT cancelled.
        throw new OperationCanceledException("handler-internal cancellation");
    }
}

[NotificationHandlerOrder(1)]
public sealed class ParallelCancellingSiblingHandler : INotificationHandler<ParallelCancellingEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(ParallelCancellingEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.CompletedTask;
    }
}

// A sibling that fails with an ordinary exception: its fault must never be lost to the
// OCE-throwing sibling (the historical bug rethrew the OCE unwrapped and dropped this).
[NotificationHandlerOrder(2)]
public sealed class ParallelCancellingFaultingHandler : INotificationHandler<ParallelCancellingEvent>
{
    public ValueTask HandleAsync(ParallelCancellingEvent notification, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("sibling failure");
    }
}

[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record SequentialCancellingEvent(string Message) : INotification;

public sealed class SequentialCancellingFirstHandler : INotificationHandler<SequentialCancellingEvent>
{
    public ValueTask HandleAsync(SequentialCancellingEvent notification, CancellationToken cancellationToken = default)
    {
        // Unrelated token: the publish CancellationToken itself is NOT cancelled.
        throw new OperationCanceledException("handler-internal cancellation");
    }
}

[NotificationHandlerOrder(1)]
public sealed class SequentialCancellingSecondHandler : INotificationHandler<SequentialCancellingEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(SequentialCancellingEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.CompletedTask;
    }
}

#endregion

#region Parallel pre-cancelled token fixtures (B3)

// Parallel publishing must reject an already-cancelled publish token before starting any
// handler, matching the Sequential/StopOnFirst entry check. These handlers are deliberately
// ct-agnostic and synchronous so the only cancellation check in play is the mediator's own.
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
public record ParallelPreCancelEvent(string Message) : INotification;

public sealed class ParallelPreCancelHandler1 : INotificationHandler<ParallelPreCancelEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(ParallelPreCancelEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.CompletedTask;
    }
}

[NotificationHandlerOrder(1)]
public sealed class ParallelPreCancelHandler2 : INotificationHandler<ParallelPreCancelEvent>
{
    public static bool WasCalled { get; private set; }
    public static void Reset() => WasCalled = false;

    public ValueTask HandleAsync(ParallelPreCancelEvent notification, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ValueTask.CompletedTask;
    }
}

#endregion
