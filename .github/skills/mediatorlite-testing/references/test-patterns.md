# Test Patterns Reference

## DI Setup Templates

### Reflection Path (manual registration)

```csharp
[Fact]
public async Task SendAsync_WithValidRequest_ReturnsResponse()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserQueryHandler>();
    services.AddMediatorLite();
    services.AddLogging();

    var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    // Act
    var result = await mediator.SendAsync(new GetUserQuery(42));

    // Assert
    result.Should().NotBeNull();
    result.Id.Should().Be(42);
}
```

### Reflection Path with options

```csharp
[Fact]
public async Task PublishAsync_RespectsHandlerOrder()
{
    // Arrange
    FirstHandler.Reset();
    SecondHandler.Reset();

    var services = new ServiceCollection();
    services.AddTransient<INotificationHandler<UserCreatedNotification>, FirstHandler>();
    services.AddTransient<INotificationHandler<UserCreatedNotification>, SecondHandler>();
    services.AddMediatorLite(options =>
    {
        options.NotificationExecutionStrategy = NotificationExecutionStrategy.Sequential;
    });
    services.AddLogging();

    var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    // Act
    await mediator.PublishAsync(new UserCreatedNotification(1, "test@test.com"));

    // Assert
    FirstHandler.CallOrder.Should().ContainInOrder(1, 0, 2);
}
```

### Reflection Path with behaviors

```csharp
[Fact]
public async Task Behaviors_ExecuteInRegistrationOrder()
{
    var services = new ServiceCollection();
    services.AddTransient<IRequestHandler<TestQuery, int>, TestQueryHandler>();
    services.AddTransient<IPipelineBehavior<TestQuery, int>, AddOneBehavior>();
    services.AddTransient<IPipelineBehavior<TestQuery, int>, MultiplyByTwoBehavior>();
    services.AddMediatorLite();
    services.AddLogging();

    var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    var result = await mediator.SendAsync(new TestQuery(5));

    // Handler: 5*2=10, MultiplyByTwo: 10*2=20, AddOne: 20+1=21
    result.Should().Be(21);
}
```

### Reflection Path with validation

```csharp
[Fact]
public async Task ValidationBehavior_WithInvalidRequest_ThrowsValidationException()
{
    var services = new ServiceCollection();
    services.AddTransient<IRequestHandler<CreateUserCommand, int>, CreateUserCommandHandler>();
    services.AddTransient<IValidator<CreateUserCommand>, DataAnnotationsValidator<CreateUserCommand>>();
    services.AddTransient<IPipelineBehavior<CreateUserCommand, int>, ValidationBehavior<CreateUserCommand, int>>();
    services.AddMediatorLite();
    services.AddLogging();

    var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    Func<Task> act = async () => await mediator.SendAsync(new CreateUserCommand("J", "invalid-email"));
    await act.Should().ThrowAsync<MediatorLite.Validation.ValidationException>();
}
```

### Source-Generation Path

```csharp
[Fact]
public async Task SendAsync_WithSourceGeneration_ReturnsResponse()
{
    var services = new ServiceCollection();
    services.AddGeneratedHandlers();   // <-- source-gen registration
    services.AddMediatorLite();
    services.AddLogging();

    var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    var result = await mediator.SendAsync(new GetUserByIdQuery(42));

    result.Should().NotBeNull();
    result.Id.Should().Be(42);
}
```

### Source-Generation Path with per-test behaviors

```csharp
[Fact]
public async Task Behaviors_ExecuteInRegistrationOrder_WithSourceGen()
{
    var services = new ServiceCollection();
    services.AddGeneratedHandlers();
    // Behaviors marked [MediatorGeneration(Skip=true)] are registered manually:
    services.AddTransient<IPipelineBehavior<ComputeValueQuery, int>, AddOneBehavior>();
    services.AddTransient<IPipelineBehavior<ComputeValueQuery, int>, MultiplyByTwoBehavior>();
    services.AddMediatorLite();
    services.AddLogging();

    var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    var result = await mediator.SendAsync(new ComputeValueQuery(5));
    result.Should().Be(21);
}
```

---

## Handler Tracking Pattern

Static tracking properties enable tests to verify handler execution without instance access:

```csharp
[MediatorGeneration(Skip = true)]
public class FirstHandler : INotificationHandler<UserCreatedNotification>
{
    public static List<int> CallOrder { get; } = [];
    public static int CallCount { get; private set; }

    public ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken cancellationToken = default)
    {
        CallCount++;
        CallOrder.Add(1);
        return ValueTask.CompletedTask;
    }

    public static void Reset()
    {
        CallCount = 0;
        CallOrder.Clear();
    }
}
```

**Variants used across the codebase:**

| Property | Type | Purpose |
|---|---|---|
| `WasCalled` | `bool` | Binary execution check |
| `CallCount` | `int` | Execution count |
| `CallOrder` | `List<int>` | Ordered execution tracking across multiple handlers |
| `WasExecuted` | `bool` | Same as WasCalled (used in validation handler types) |
| `ExecutionLog` | `List<string>` | Detailed execution log with phase names |
| `Calls` | `List<string>` | Similar to ExecutionLog (used in generic behaviors) |
| `LastCreatedId` / `LastDeletedId` | `int?` | Tracks last processed value |

Always call `Reset()` at the start of Arrange, before ServiceCollection creation.

---

## Inline Test Type Conventions

### Reflection tests: inline types with `#region`

Reflection test files define their types directly in the test file using `#region` blocks:

```csharp
namespace MediatorLite.Tests.Reflection;

public class MediatorTests
{
    #region Test Request/Handler Types

    public record GetUserQuery(int Id) : IRequest<User>;
    public record User(int Id, string Name);

    [MediatorGeneration(Skip = true)]
    public class GetUserQueryHandler : IRequestHandler<GetUserQuery, User>
    {
        public ValueTask<User> HandleAsync(GetUserQuery request, CancellationToken ct = default)
        {
            return ValueTask.FromResult(new User(request.Id, "Test User"));
        }
    }

    #endregion

    [Fact]
    public async Task SendAsync_WithValidRequest_ReturnsResponse() { … }
}
```

Key rules:
- Request/response records: no `[MediatorGeneration(Skip = true)]` needed (they're not handlers)
- Handler and behavior classes: **always** `[MediatorGeneration(Skip = true)]`
- Types can be nested inside the test class or sibling classes in the same file
- Each file is self-contained — types are not shared across Reflection test files

### SourceGeneration tests: centralized `TestTypes.cs`

All types live in `SourceGeneration/TestTypes.cs` with `#region` blocks:
- `#region Request/Response Types`
- `#region Notification Types`
- `#region Request Handlers`
- `#region Notification Handlers`
- `#region Pipeline Behaviors`
- `#region Validation Types`

Test files in `SourceGeneration/` import these types by namespace (`MediatorLite.Tests.SourceGeneration`).

---

## `[Fact]`-Only Convention

The test project uses exclusively `[Fact]` attributes. These are **not used**:
- `[Theory]`
- `[InlineData]`
- `[ClassData]`
- `[MemberData]`

Each test is a standalone `[Fact]` method, even when testing multiple variants of the same behavior.

---

## No Test Fixtures or Shared Infrastructure

The following patterns are **not used** in this project:
- `IClassFixture<T>` / `ICollectionFixture<T>`
- `IAsyncLifetime`
- Shared base test classes
- Helper utility classes or methods
- Shared `ServiceProvider` instances

Every test method creates its own `ServiceCollection` and `ServiceProvider` from scratch.

---

## FluentAssertions Patterns

```csharp
// Value equality
result.Should().Be(42);
result.Should().NotBeNull();
result.Should().Be(Unit.Value);

// Property assertions
result.Id.Should().Be(42);
result.Name.Should().Be("Test User");

// Boolean
condition.Should().BeTrue();
condition.Should().BeFalse();

// Collection
callOrder.Should().ContainInOrder(1, 0, 2);
errors.Should().HaveCount(2);
errors.Should().HaveCountGreaterThanOrEqualTo(2);
errors.Should().ContainSingle();
errors.Should().BeEmpty();
errors.Should().Contain(e => e.ErrorMessage.Contains("admin"));
calls.Should().Contain("Before: TestQuery");

// Async exception
Func<Task> act = async () => await mediator.SendAsync(new FailingQuery());
await act.Should().ThrowAsync<InvalidOperationException>()
    .WithMessage("*Handler failed*");

// Chained exception inspection
var exception = await act.Should().ThrowAsync<AggregateException>();
exception.Which.InnerExceptions.Should().ContainSingle()
    .Which.Should().BeOfType<InvalidOperationException>()
    .Which.Message.Should().Contain("Handler failed");

// No-throw
await act.Should().NotThrowAsync();

// String contains
exception.Message.Should().Contain("2 errors");
```

---

## xUnit Assert Patterns

```csharp
// Equality
Assert.Equal(expected, actual);
Assert.NotEqual(a, b);

// Boolean
Assert.True(condition);
Assert.False(condition);

// Reference
Assert.NotNull(obj);
Assert.Same(obj1, obj2);
Assert.NotSame(obj1, obj2);

// Type
Assert.IsType<FailingRequest>(request);

// String
Assert.Contains("substring", str);
Assert.NotEmpty(str);

// Exceptions
var ex = Assert.Throws<ArgumentNullException>(() => options.AddOpenBehavior(null!));
Assert.NotNull(ex);

// Collection
Assert.Single(collection);
Assert.Empty(collection);
```
