---
name: mediatorlite-tests
description: Reference for the MediatorLite.Tests project -- directory layout (SourceGeneration/ vs UnitTests/), xUnit + FluentAssertions conventions, TestTypes.cs patterns (handler tracking via static state, notification recorders, compile-time strategy attributes, behavior ordering), writing new tests for requests / behaviors / notifications / validators, sanity checks using MediatorLiteRegistration.*Count, and the fact that [MediatorGeneration(Skip=true)] is obsolete.
triggers: MediatorLite tests, xUnit, FluentAssertions, TestTypes, source-gen tests, notification tests, handler tracking pattern, MediatorLiteRegistration.RequestHandlerCount, MediatorLiteRegistration.NotificationHandlerCount, PipelineBehaviorTests, ValidationTests, NotificationTests, UnitTests, AttributeTests, DtoPropertySettersTests, MediatorGeneration obsolete
---

# MediatorLite.Tests

## Purpose

`MediatorLite.Tests` is the sole xUnit test project for the library. It has two concerns: (1) exercising the full source-generated dispatch pipeline end-to-end (`SourceGeneration/`) and (2) validating pure value-type surface (`UnitTests/`). The project **must** have the source generator as an analyzer — every test type declared in `TestTypes.cs` is discovered at compile time and populates `MediatorLiteRegistration`. The sanity tests on `*Count` properties are both assertions and guardrails: they fail instantly if the generator regresses.

## When to use

- Adding a new handler, behavior, validator, or notification to verify wiring.
- Reproducing a bug in dispatch, pipeline composition, validation, or notification strategy.
- Validating generator output via `MediatorLiteRegistration.RequestHandlerCount` / `NotificationHandlerCount` / `BehaviorCount` / `ValidatorCount`.
- Writing a pure unit test for an attribute, `ValidationResult`/`ValidationError`/`ValidationException`, `Unit`, or DTO record semantics.

## Project location & entry points

- [MediatorLite.Tests.csproj](tests/MediatorLite.Tests/MediatorLite.Tests.csproj)
- **SourceGeneration end-to-end tests** — live integration of the mediator + generator + DI:
  - [TestTypes.cs](tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs) — all shared request / notification / handler / behavior / validator types.
  - [MediatorTests.cs](tests/MediatorLite.Tests/SourceGeneration/MediatorTests.cs) — `SendAsync` coverage.
  - [NotificationTests.cs](tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs) — `PublishAsync` coverage, per-type strategies.
  - [PipelineBehaviorTests.cs](tests/MediatorLite.Tests/SourceGeneration/PipelineBehaviorTests.cs) — behavior ordering, short-circuit, open generic composition.
  - [ValidationTests.cs](tests/MediatorLite.Tests/SourceGeneration/ValidationTests.cs) — `ValidationBehavior`, DataAnnotations auto-wire, custom `IValidator<T>`.
- **UnitTests** — narrow, DI-free tests:
  - [AttributeTests.cs](tests/MediatorLite.Tests/UnitTests/AttributeTests.cs)
  - [ValidationTests.cs](tests/MediatorLite.Tests/UnitTests/ValidationTests.cs)
  - [DtoPropertySettersTests.cs](tests/MediatorLite.Tests/UnitTests/DtoPropertySettersTests.cs)
- Conventions: **xUnit** (`[Fact]`, `[Theory]`, `[InlineData]`), **FluentAssertions** (`.Should().…`), `Arrange / Act / Assert` comments.

## Core types / API surface

### Canonical end-to-end test shape

`MediatorTests.cs` demonstrates the exact DI bootstrap every source-gen test uses:

```31:50:tests/MediatorLite.Tests/SourceGeneration/MediatorTests.cs
    public void AddGeneratedHandlers_RegistersSourceGeneratedMediator()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();

        // Act
        var mediator = provider.GetService<IMediator>();

        // Assert
        mediator.Should().NotBeNull(
            "AddGeneratedHandlers should register the source-generated IMediator for zero-reflection dispatch");
        mediator.Should().BeOfType<SourceGeneratedMediator>(
            "the generated mediator must win over the AddMediatorLite() diagnostic fallback");
    }
```

The generated `MediatorLite.Generated.SourceGeneratedMediator` *is* the `IMediator` — there is no separate `ISourceGeneratedMediator` to resolve. The test imports `using MediatorLite.Generated;` for `SourceGeneratedMediator`.

`AddLogging()` is required — the emitted pipeline resolves `ILogger<IMediator>` when logging is enabled (default).

### Sanity-check pattern — `*Count` assertions

These tests catch generator regressions without any DI spin-up:

```16:29:tests/MediatorLite.Tests/SourceGeneration/MediatorTests.cs
    [Fact]
    public void AddGeneratedHandlers_RegistersRequestHandlers()
    {
        // Assert that source generator discovered our handlers
        MediatorLiteRegistration.RequestHandlerCount.Should().BeGreaterThan(0,
            "Source generator should discover request handlers at compile-time");
    }

    [Fact]
    public void AddGeneratedHandlers_RegistersNotificationHandlers()
    {
        // Assert that source generator discovered notification handlers
        MediatorLiteRegistration.NotificationHandlerCount.Should().BeGreaterThan(0,
            "Source generator should discover notification handlers at compile-time");
    }
```

All four available counts are `MediatorLiteRegistration.RequestHandlerCount`, `NotificationHandlerCount`, `BehaviorCount`, `ValidatorCount`.

### `TestTypes.cs` patterns

#### Request records

Requests are records implementing `IRequest<TResponse>`; `IRequest` (= `IRequest<Unit>`) for commands:

```11:26:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
public record GetUserByIdQuery(int Id) : IRequest<UserDto>;

public record UserDto(int Id, string Name, string Email);

public record CreateUserCommand(string Name, string Email) : IRequest<int>;

public record DeleteUserByIdCommand(int Id) : IRequest;

public record FailingRequest : IRequest<string>;

public record ComputeValueQuery(int Value) : IRequest<int>;

public record DelayedRequest : IRequest<string>;

public record ShortCircuitQuery : IRequest;
```

#### Handler tracking pattern (static state + `Reset()`)

Every handler used to verify invocation has `public static bool WasCalled` (or a counter / list) and a `public static void Reset()` method. Call `Reset()` in the `// Arrange` block before each test:

```96:108:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
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
```

For ordering tests, notification handlers append to a **shared** static `List<int> CallOrder` on `UserCreatedEventHandler1`:

```140:180:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
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
```

#### Notification strategy attributes (compile-time only)

```31:62:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
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
```

#### Behavior ordering + short-circuit

```342:366:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
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
```

A short-circuit behavior simply omits the `next()` call:

```386:398:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
[BehaviorOrder(1)]
public class ShortCircuitBehavior : IPipelineBehavior<ShortCircuitQuery, Unit>
{
    public static bool Executed = false;
    public ValueTask<Unit> HandleAsync(
        ShortCircuitQuery request,
        RequestHandlerDelegate<Unit> next,
        CancellationToken cancellationToken = default)
    {
        Executed = true;
        return Unit.CompletedTask;
    }
}
```

#### Validation types — DataAnnotations + custom validator

DataAnnotations on a request record trigger **automatic** `DataAnnotationsValidator<T>` registration:

```422:430:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
public sealed record ValidatedCommand : IRequest<string>
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 50 characters")]
    public required string Name { get; init; }

    [Range(1, 100, ErrorMessage = "Value must be between 1 and 100")]
    public int Value { get; init; }
}
```

A non-generic, closed `IValidator<T>` implementation is discovered by the generator and merged with the DataAnnotations validator under the same `ValidationBehavior<TReq, TRes>`:

```450:467:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
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
```

### Notification strategy tests

`NotificationTests.cs` exercises every strategy permutation. The `ParallelEvent` test expects `AggregateException` because the event is configured with `ContinueAndAggregate`:

```113:134:tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs
    [Fact]
    public async Task PublishAsync_WithParallelStrategy_AllHandlersRun()
    {
        // Arrange
        ParallelEventSuccessHandler.Reset();

        var services = new ServiceCollection();
        services.AddGeneratedHandlers();
        services.AddMediatorLite();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert - ParallelEvent has ContinueAndAggregate, so success handler should run
        // even though failing handler throws
        Func<Task> act = async () => await mediator.PublishAsync(new ParallelEvent("test"));
        await act.Should().ThrowAsync<AggregateException>();

        ParallelEventSuccessHandler.WasCalled.Should().BeTrue(
            "Success handler should run even when another handler fails with ContinueAndAggregate strategy");
    }
```

`PublishAsync_WithNoHandlers_CompletesWithoutError` guards the contract that `PublishAsync` on a notification with zero handlers is a **no-op**, not an exception:

```96:111:tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs
    [Fact]
    public async Task PublishAsync_WithNoHandlers_CompletesWithoutError()
    {
        // Arrange - Create a notification type that has no handlers registered
        var services = new ServiceCollection();
        services.AddMediatorLite();
        services.AddGeneratedHandlers();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        Func<Task> act = async () => await mediator.PublishAsync(new UserCreatedEvent(1, "test@test.com"));
        await act.Should().NotThrowAsync();
    }
```

## Patterns & invariants

**Do:**
- Always call `services.AddGeneratedHandlers().AddMediatorLite().AddLogging()` (order matters: generator registration before mediator; logging is mandatory when observability is on, which is the default for the test assembly).
- Call `HandlerName.Reset()` in the `// Arrange` block of every test that reads static handler state (xUnit does not isolate instance statics).
- Use records for request / notification types for succinct definitions and value-equality.
- Use `[BehaviorOrder(n)]` on every behavior — order is **compile-time resolved**; do not rely on source file order.
- Use `[NotificationExecution]` and `[NotificationError]` on the notification **type**, not on the handler.
- Assert on `MediatorLiteRegistration.RequestHandlerCount` etc. whenever a PR adds a whole new category.

**Don't:**
- Don't add `[MediatorGeneration(Skip = true)]` to new test types — the attribute is obsolete and the generator ignores it entirely (discovery in `GetHandlerInfo` is unconditional); a "skipped" handler is registered like any other.
- Don't share mutable instance state between handlers; use static fields + `Reset()`.
- Don't expect dispatch without `AddGeneratedHandlers()` — without it the only `IMediator` is the [ThrowingMediator](src/MediatorLite/Internal/ThrowingMediator.cs) fallback (registered by `AddMediatorLite()`), which throws with a specific setup-guidance message on every dispatch.
- Don't put `[NotificationExecution]` on a notification handler — the generator only reads it off the `INotification` implementation.

## Common tasks

1. **Add a new request test**
   1. In [TestTypes.cs](tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs) under `#region Request/Response Types`, add `public record MyQuery(...) : IRequest<MyResponse>;`.
   2. Under `#region Request Handlers`, add a handler class with static tracking state + `Reset()`.
   3. Create `MyQueryTests.cs` (or add a `[Fact]` to an existing file), bootstrapping the DI container with `AddGeneratedHandlers().AddMediatorLite().AddLogging()`.
   4. Act: `await mediator.SendAsync(new MyQuery(...))`.
   5. Assert: result + handler tracking flags via FluentAssertions.

2. **Add a behavior test**
   1. Define a behavior with `[BehaviorOrder(n)]` in `TestTypes.cs` (closed or open generic).
   2. Write a `[Fact]` that composes the behavior with a known handler, and assert via static `Calls` / `ExecutionLog` list.
   3. For short-circuit: do **not** call `next()` inside the behavior, assert that the downstream handler was not invoked.

3. **Add a notification strategy test**
   1. Define a new notification record with `[NotificationExecution(...)]` / `[NotificationError(...)]`.
   2. Add handlers (with `[NotificationHandlerOrder(n)]` for ordering tests).
   3. Assert either `NotThrowAsync`, `ThrowAsync<InvalidOperationException>`, or `ThrowAsync<AggregateException>` depending on the strategy.

4. **Add a validator test**
   1. Add DataAnnotations to your request record — no extra code needed for auto-registration.
   2. For business rules, implement `IValidator<MyRequest>` (non-generic closed) — the generator picks it up.
   3. Assert that `ValidationException` is thrown with expected `Errors`.

5. **Troubleshoot a missing handler**
   1. Check `MediatorLiteRegistration.RequestHandlerCount` — if it didn't increase, the generator didn't see your type.
   2. Ensure the handler is `public`, non-`abstract`, and has no `[MediatorGeneration(Skip = true)]`.
   3. Rebuild — Roslyn caches incremental generator output; a full rebuild forces regeneration.
   4. Read `obj/Debug/netX/generated/MediatorLite.SourceGeneration/MediatorLite.SourceGeneration.HandlerDiscoveryGenerator/MediatorLiteRegistration.g.cs`.

## Pitfalls & gotchas

- **Static state bleeds across tests**: xUnit creates a new instance per test but static fields persist. Always `Reset()` before acting. Parallelism at the test class level can race static state — keep handlers dedicated to one notification or guard with per-test names.
- **Test projects target `net10.0`** (see [Directory.Build.props](Directory.Build.props)) with warnings-as-errors. Any new type that would produce a nullable-ref warning fails the build.
- **`[MediatorGeneration(Skip = true)]` is obsolete and inert** (documented in [Attributes.cs](src/MediatorLite.Abstractions/Abstractions/Attributes.cs)). The generator ignores it entirely — a "skipped" handler is registered like any other. Never add it to new test types.
- **`MediatorLiteRegistration` is generated per-assembly**. The counts reflect handlers in `MediatorLite.Tests` **only** — not downstream projects. Likewise, changing a handler in another assembly does not regenerate the tests' registration.
- **Generator projects must be `netstandard2.0`**; test types, however, target `net10.0`. Stay on the test-assembly side when writing dependent code.
- **`[NotificationHandlerOrder(n)]` with equal values**: order is **stable but not strictly defined** by the spec — in practice the generator orders by `OrderBy(h => h.Order ?? 0)` which preserves discovery order for ties. Do not rely on a specific tie-break.
- **`PublishAsync_WithNoHandlers` must not throw**: if you add a `throw` when the publisher dictionary misses a type, it breaks this contract.
- **`AddLogging()` is required** because the generator emits `sp.GetRequiredService<ILogger<IMediator>>()` in the default-enabled logging path. Omitting it throws `InvalidOperationException` from DI. If you want tests without logging, add `[assembly: DisableMediatorLogging]` — but do not do this in `MediatorLite.Tests` because the existing logging tests require it.
- **FluentAssertions `.Should().ThrowAsync<AggregateException>()`** does **not** automatically flatten. If you need to inspect inner exceptions, use `.WithInnerException<InvalidOperationException>()` or inspect `.InnerExceptions` yourself.

## Related skills & rules

- **mediatorlite-abstractions** — defines every interface and attribute used in `TestTypes.cs`.
- **mediatorlite-core** — `Mediator` implementation, `ValidationBehavior`, `DataAnnotationsValidator` behaviors under test.
- **mediatorlite-source-generation** — the generator under test; `MediatorLiteRegistration.*Count` assertions depend on its emission.
- **mediatorlite-sample-sourcegen** — the same patterns applied in a real app, useful as a bigger-scale example.
- [AGENTS.md](AGENTS.md): "Test layout: `tests/MediatorLite.Tests/{SourceGeneration,UnitTests}`. `MediatorLiteRegistration.RequestHandlerCount`, `NotificationHandlerCount`, `BehaviorCount`, and `ValidatorCount` are useful sanity checks".
- Docs: [docs/notifications.md](docs/notifications.md), [docs/validation.md](docs/validation.md), [docs/pipeline-behaviors.md](docs/pipeline-behaviors.md).
