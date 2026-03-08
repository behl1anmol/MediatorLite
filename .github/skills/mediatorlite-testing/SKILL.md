---
name: mediatorlite-testing
description: >
  Use this skill whenever writing, modifying, or debugging tests for MediatorLite,
  including unit tests, integration tests for reflection-based or source-generated dispatch,
  pipeline behavior tests, notification tests, or validation tests. Also use when figuring out
  how to structure test types, whether to use [MediatorGeneration(Skip=true)], or understanding
  existing test patterns and coverage.
---

# MediatorLite Testing Guide

## Test Organization

The test project (`tests/MediatorLite.Tests/`) is organized into three directories:

| Directory | Purpose | Assertion style |
|---|---|---|
| `Reflection/` | Integration tests for **manual DI / reflection-based** dispatch. Handlers registered explicitly via `services.AddTransient<IRequestHandler<…>, Handler>()`. | FluentAssertions (except `MediatorOptionsTests` and `ServiceCollectionExtensionsTests` which use xUnit `Assert`) |
| `SourceGeneration/` | Integration tests for **source-generated** dispatch. Handlers auto-discovered by `AddGeneratedHandlers()`. | FluentAssertions |
| `UnitTests/` | Pure unit tests — no DI container, no mediator. Covers attributes, DTOs, and validation model types. | xUnit `Assert` |

**Why three directories?** The source generator runs on the entire test assembly. Reflection tests must opt their handlers *out* of source-gen discovery so they can control registration manually. SourceGeneration tests rely on auto-discovery. UnitTests have no DI at all.

## Test Stack

| Package | Version | Notes |
|---|---|---|
| xunit | 2.7.0 | Test framework |
| FluentAssertions | 6.12.0 | Fluent assertion library for integration tests |
| NSubstitute | 5.1.0 | Referenced in csproj but **not currently used** in any test |
| Microsoft.Extensions.DependencyInjection | 9.0.0 | DI container for integration tests |
| Microsoft.Extensions.Logging | 9.0.0 | Required by `AddMediatorLite()` |
| Microsoft.NET.Test.Sdk | 17.9.0 | Test runner infrastructure |
| coverlet.collector | 6.0.1 | Code coverage |

The source generator project is also referenced as an analyzer:
```xml
<ProjectReference Include="..\..\src\MediatorLite.SourceGeneration\MediatorLite.SourceGeneration.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## DI Setup Pattern

Every integration test builds its own isolated `ServiceProvider`. Template:

```csharp
// Arrange
var services = new ServiceCollection();

// --- Registration (pick ONE path) ---

// Reflection path:
services.AddTransient<IRequestHandler<MyQuery, MyResult>, MyQueryHandler>();

// Source-gen path:
services.AddGeneratedHandlers();

// --- Common tail ---
services.AddMediatorLite();          // or AddMediatorLite(options => { … })
services.AddLogging();
var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

// Act
var result = await mediator.SendAsync(new MyQuery(…));

// Assert
result.Should().Be(expected);
```

Behaviors are registered **before** `AddMediatorLite()` when using manual DI, or via `options.AddOpenBehavior()`. Validators follow the same pattern.

## Critical Convention: `[MediatorGeneration(Skip = true)]`

Because the source generator scans the entire test assembly, every handler/behavior
defined inside `Reflection/` tests **must** be decorated with `[MediatorGeneration(Skip = true)]`.
Without it the source generator would auto-register those types, breaking the Reflection tests'
assumption that only manually-registered handlers exist.

### Rules by directory

| Directory | Handler types | `[MediatorGeneration(Skip = true)]` |
|---|---|---|
| `Reflection/` | Defined inline (nested classes or sibling classes inside `#region` blocks in the same file) | **All handlers and behaviors** must have it |
| `SourceGeneration/` | Centralized in `TestTypes.cs` | Only behaviors that need per-test control (e.g., `AddOneBehavior`, `ShortCircuitBehavior`, `GenericLoggingBehavior`, `ExecutionOrderTrackingBehavior`). Request handlers and notification handlers are left **without** it so the generator discovers them. |
| `UnitTests/` | No handlers; tests use types from `SourceGeneration/TestTypes.cs` or construct attribute/DTO instances directly | N/A |

### Why behaviors get Skip in SourceGeneration tests

Behaviors marked `Skip = true` in `TestTypes.cs` are behaviors each test wants to register
selectively (e.g., one test adds `AddOneBehavior`, another adds `ShortCircuitBehavior`).
If they were auto-registered, every test would have all behaviors in the pipeline, making
targeted assertions impossible.

## Test Naming

All tests follow `MethodUnderTest_Scenario_ExpectedBehavior`:

```
SendAsync_WithValidRequest_ReturnsResponse
PublishAsync_StopOnFirst_StopsAfterFirstSuccess
ValidationBehavior_ShortCircuitsOnDataAnnotationFailure_BeforeOtherBehaviors
AddOpenBehavior_WithNonGenericType_ThrowsArgumentException
```

## Assertion Conventions

**FluentAssertions** (integration tests):
```csharp
result.Should().Be(42);
result.Should().NotBeNull();
result.Id.Should().Be(42);
await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Handler failed*");
callOrder.Should().ContainInOrder(1, 0, 2);
exception.Which.Errors.Should().Contain(e => e.ErrorMessage.Contains("admin"));
```

**xUnit Assert** (unit tests, `MediatorOptionsTests`, `ServiceCollectionExtensionsTests`):
```csharp
Assert.Equal(expected, actual);
Assert.True(condition);
Assert.NotNull(obj);
Assert.Throws<ArgumentException>(() => …);
Assert.Same(obj1, obj2);
Assert.Contains("substring", str);
```

## Handler Tracking Pattern

Handlers that need to report back to tests use **static** tracking properties with a `Reset()` method:

```csharp
[MediatorGeneration(Skip = true)]
public class MyHandler : INotificationHandler<MyNotification>
{
    public static bool WasCalled { get; private set; }
    public static int CallCount { get; private set; }
    public static List<int> CallOrder { get; } = [];

    public static void Reset()
    {
        WasCalled = false;
        CallCount = 0;
        CallOrder.Clear();
    }

    public ValueTask HandleAsync(MyNotification notification, CancellationToken ct = default)
    {
        WasCalled = true;
        CallCount++;
        CallOrder.Add(1);
        return ValueTask.CompletedTask;
    }
}
```

Every test that uses a tracked handler **must** call `Reset()` at the start of the Arrange section. Static state is shared across tests; without `Reset()` ordering-dependent failures will appear.

## How to Write New Tests

### Adding a Reflection test

1. Create or edit a file in `Reflection/`.
2. Define test types inline — use `#region Test Types` blocks.
3. Mark **every** handler and behavior with `[MediatorGeneration(Skip = true)]`.
4. Register handlers manually via `services.AddTransient<IRequestHandler<…>, …>()`.
5. Follow the DI setup pattern above.
6. Use `[Fact]` only — no `[Theory]`, `[InlineData]`, `[ClassData]`, or `[MemberData]`.
7. Use FluentAssertions (unless testing options/DI registration, where xUnit Assert is used).

### Adding a SourceGeneration test

1. Add new request/handler/notification types to `SourceGeneration/TestTypes.cs`.
2. Do **not** add `[MediatorGeneration(Skip = true)]` to request handlers or notification handlers (they must be source-gen discovered).
3. Mark behaviors `Skip = true` only if they should be registered selectively per-test.
4. Use `services.AddGeneratedHandlers()` in the Arrange section.
5. Optionally verify source-gen APIs like `TrySendAsync`, `TryGetHandlerOrder`, `TryGetNotificationOptions`, `TryResolveBehaviors`, `MediatorLiteRegistration.RequestHandlerCount`, etc.

### Adding a UnitTest

1. Create or edit a file in `UnitTests/`.
2. No DI container — construct objects directly.
3. Use xUnit `Assert.*` methods.
4. Reuse types from `SourceGeneration/TestTypes.cs` where applicable.

### General rules for all categories

- Every test is **fully isolated** — builds its own `ServiceProvider`.
- No shared base classes, no helper utility classes.
- No `IClassFixture<T>`, `ICollectionFixture<T>`, or `IAsyncLifetime`.
- Each test method is `[Fact]` only.
- Framework: net10.0 with nullable enabled, implicit usings.

## References

For detailed patterns and examples, read these reference files:

- [references/test-patterns.md](references/test-patterns.md) — DI setup templates, handler tracking, inline type conventions, assertion patterns
- [references/coverage-map.md](references/coverage-map.md) — Complete feature coverage matrix, known gaps, file/method counts
- [references/source-gen-vs-reflection.md](references/source-gen-vs-reflection.md) — Side-by-side comparison, TestTypes.cs structure, source-gen API testing
