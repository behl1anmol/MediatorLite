# Source-Generation vs Reflection Testing

## Side-by-Side Comparison

| Aspect | Reflection Tests | SourceGeneration Tests |
|---|---|---|
| **Directory** | `Reflection/` | `SourceGeneration/` |
| **Handler registration** | `services.AddTransient<IRequestHandler<…>, …>()` | `services.AddGeneratedHandlers()` |
| **Test type location** | Inline in each test file (nested classes or sibling classes inside `#region` blocks) | Centralized in `TestTypes.cs` |
| **`[MediatorGeneration(Skip = true)]`** | On **every** handler and behavior | Only on behaviors needing per-test control |
| **Dispatch path** | Reflection-based with `ConcurrentDictionary` caching | Zero-reflection via `ISourceGeneratedMediator` |
| **ISourceGeneratedMediator** | Not registered, not tested | Registered and tested via `TrySendAsync`, `TryInvokeHandlerAsync`, etc. |
| **MediatorLiteRegistration** | Not used | Verified: `RequestHandlerCount`, `NotificationHandlerCount`, `ValidatorCount` |
| **Behavior registration** | `services.AddTransient<IPipelineBehavior<…>, …>()` | Same (behaviors with `Skip=true` are registered manually per-test) |
| **Validator registration** | Manual: `services.AddTransient<IValidator<…>, …>()` + `services.AddTransient<IPipelineBehavior<…>, ValidationBehavior<…>>()` | Auto-discovered by source generator |
| **Assertion style** | FluentAssertions (mostly), xUnit Assert for Options/DI tests | FluentAssertions |

## TestTypes.cs Structure

`SourceGeneration/TestTypes.cs` organizes all shared types in `#region` blocks:

```
#region Request/Response Types
    GetUserByIdQuery, UserDto, CreateUserCommand, DeleteUserByIdCommand,
    FailingRequest, ComputeValueQuery, DelayedRequest

#region Notification Types
    UserCreatedEvent
    ParallelEvent          [NotificationOptions(Parallel, ContinueAndAggregate, OverrideGlobal=true)]
    StopOnFirstEvent       [NotificationOptions(StopOnFirst, OverrideGlobal=true)]
    StopOnFirstFallbackEvent [NotificationOptions(StopOnFirst, ContinueAndAggregate, OverrideGlobal=true)]

#region Request Handlers
    GetUserByIdQueryHandler, CreateUserCommandHandler, DeleteUserByIdCommandHandler,
    FailingRequestHandler, ComputeValueQueryHandler, DelayedRequestHandler

#region Notification Handlers
    UserCreatedEventHandler1 (no order attr, tracks CallOrder + CallCount)
    UserCreatedEventHandler2 [NotificationHandlerOrder(1)]
    UserCreatedEventHandler3 [NotificationHandlerOrder(2)]
    ParallelEventFailingHandler, ParallelEventSuccessHandler
    StopOnFirstEventHandler1, StopOnFirstEventHandler2 [Order(1)]
    StopOnFirstFallbackEventHandler1 (always throws)
    StopOnFirstFallbackEventHandler2 [Order(1)] (succeeds)
    StopOnFirstFallbackEventHandler3 [Order(2)] (should not be reached)

#region Pipeline Behaviors  ← ALL marked [MediatorGeneration(Skip = true)]
    AddOneBehavior, MultiplyByTwoBehavior, GenericLoggingBehavior<,>,
    ShortCircuitBehavior, ExecutionOrderTrackingBehavior<,>

#region Validation Types
    ValidatedCommand (with [Required], [StringLength], [Range] DataAnnotations)
    ValidatedCommandHandler (tracks WasExecuted)
    ValidatedCommandCustomValidator (rejects names containing "blocked")
```

### Why behaviors have `Skip = true` in TestTypes.cs

Each SourceGeneration test needs to control which behaviors are in the pipeline. If `AddOneBehavior` and `MultiplyByTwoBehavior` were auto-registered, every test would have both behaviors active, making targeted assertions impossible. By marking them `Skip = true`, tests register only the specific behaviors they need:

```csharp
// Test wants ONLY AddOneBehavior:
services.AddGeneratedHandlers();
services.AddTransient<IPipelineBehavior<ComputeValueQuery, int>, AddOneBehavior>();
```

### Why request handlers do NOT have `Skip = true` in TestTypes.cs

Request handlers and notification handlers are the whole point of source-gen testing — they must be auto-discovered. Every SourceGeneration test relies on `AddGeneratedHandlers()` finding these types.

## What SourceGeneration Tests Additionally Verify

Beyond the functional behavior tested in both paths, SourceGeneration tests verify source-gen-specific APIs:

### ISourceGeneratedMediator API

```csharp
// TrySendAsync — verify source-gen can dispatch a request type
var canDispatch = sourceGenMediator?.TrySendAsync<UserDto>(
    provider, new GetUserByIdQuery(1), CancellationToken.None);
canDispatch.Should().NotBeNull();

// TryInvokeHandlerAsync — verify source-gen can invoke handler directly
var canInvoke = sourceGenMediator.TryInvokeHandlerAsync<UserDto>(
    provider, new GetUserByIdQuery(1), CancellationToken.None);
canInvoke.Should().NotBeNull();

// TryGetHandlerOrder — verify source-gen knows handler ordering
var order = sourceGenMediator.TryGetHandlerOrder(typeof(UserCreatedEventHandler2));
order.Should().Be(1);

// TryGetNotificationOptions — verify source-gen knows per-notification options
var options = sourceGenMediator.TryGetNotificationOptions(typeof(ParallelEvent));
options.Should().NotBeNull();
options!.Value.ExecutionStrategy.Should().Be(NotificationExecutionStrategy.Parallel);

// TryResolveBehaviors — verify source-gen returns empty list for known types without behaviors
var behaviors = sourceGenMediator.TryResolveBehaviors(
    provider, typeof(ComputeValueQuery), typeof(int));
behaviors.Should().NotBeNull();
behaviors!.Count.Should().Be(0);
```

### MediatorLiteRegistration Counts

```csharp
MediatorLiteRegistration.RequestHandlerCount.Should().BeGreaterThan(0);
MediatorLiteRegistration.NotificationHandlerCount.Should().BeGreaterThan(0);
MediatorLiteRegistration.ValidatorCount.Should().BeGreaterThan(0);
```

## Additional Types Defined Outside TestTypes.cs

Some SourceGeneration test files define additional notification types inline for specific test scenarios. These are placed after the test class in `#region` blocks:

**In `SourceGeneration/NotificationTests.cs`:**
```csharp
#region Additional Test Types for StopOnFirst Error Handling

public record StopOnFirstWithErrorEvent(string Message) : INotification;
public class StopOnFirstWithErrorEventFailingHandler : INotificationHandler<StopOnFirstWithErrorEvent> { … }
public class StopOnFirstWithErrorEventSuccessHandler : INotificationHandler<StopOnFirstWithErrorEvent> { … }

public record AllFailStopOnFirstEvent(string Message) : INotification;
public class AllFailStopOnFirstEventHandler1 : INotificationHandler<AllFailStopOnFirstEvent> { … }
public class AllFailStopOnFirstEventHandler2 : INotificationHandler<AllFailStopOnFirstEvent> { … }

#endregion
```

These types are auto-discovered by the source generator (no `Skip = true`).

## How to Add New Types

### For Reflection tests

1. Define the type inline in the Reflection test file.
2. Use `#region` blocks to group types.
3. Add `[MediatorGeneration(Skip = true)]` to all handlers and behaviors.
4. Register manually in the test's Arrange section.

```csharp
// In Reflection/MyNewTests.cs
public class MyNewTests
{
    #region Test Types

    public record MyQuery(int Id) : IRequest<string>;

    [MediatorGeneration(Skip = true)]
    public class MyQueryHandler : IRequestHandler<MyQuery, string>
    {
        public ValueTask<string> HandleAsync(MyQuery request, CancellationToken ct = default)
            => ValueTask.FromResult($"Result: {request.Id}");
    }

    #endregion

    [Fact]
    public async Task SendAsync_WithMyQuery_ReturnsExpected()
    {
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<MyQuery, string>, MyQueryHandler>();
        services.AddMediatorLite();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.SendAsync(new MyQuery(5));

        result.Should().Be("Result: 5");
    }
}
```

### For SourceGeneration tests

1. Add the type to `SourceGeneration/TestTypes.cs` in the appropriate `#region`.
2. Do **not** add `[MediatorGeneration(Skip = true)]` to request handlers or notification handlers.
3. Add `[MediatorGeneration(Skip = true)]` only to behaviors you want to register per-test.
4. Use `services.AddGeneratedHandlers()` in the test — rebuilding will trigger the source generator to pick up the new type.
5. If the type is only needed by one test file and is a notification/handler pair for a narrow scenario, it can be placed inline in the test file (like `StopOnFirstWithErrorEvent` in `NotificationTests.cs`).

```csharp
// In SourceGeneration/TestTypes.cs, add to #region Request/Response Types:
public record MyNewQuery(int Id) : IRequest<string>;

// In SourceGeneration/TestTypes.cs, add to #region Request Handlers:
public class MyNewQueryHandler : IRequestHandler<MyNewQuery, string>
{
    public ValueTask<string> HandleAsync(MyNewQuery request, CancellationToken ct = default)
        => ValueTask.FromResult($"Result: {request.Id}");
}

// In SourceGeneration/MyNewTests.cs:
[Fact]
public async Task SendAsync_WithMyNewQuery_ReturnsExpected()
{
    var services = new ServiceCollection();
    services.AddGeneratedHandlers();
    services.AddMediatorLite();
    services.AddLogging();
    var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    var result = await mediator.SendAsync(new MyNewQuery(5));

    result.Should().Be("Result: 5");
}
```
