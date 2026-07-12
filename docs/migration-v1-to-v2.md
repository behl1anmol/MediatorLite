# Migration Guide: MediatorLite v1 to v2

This guide helps you migrate from MediatorLite v1 (reflection + source-gen hybrid) to v2 (source-generation only).

---

## Overview of Changes

MediatorLite v2 is a **source-generation-only** architecture. The reflection-based fallback dispatch has been removed in favor of compile-time generated dispatch code.

### What Moved from Runtime to Compile-Time

| Feature | v1 | v2 |
|---------|----|----|
| Handler discovery | Runtime reflection + optional source-gen | **Compile-time only** |
| Pipeline behavior wrapping | Runtime delegate chains | **Generated unrolled pipelines** |
| Handler resolution | `ConcurrentDictionary` caching | **Compile-time type-pattern switch** |
| Notification handler ordering | Runtime sorting | **Compile-time sorting** |

---

## Breaking Changes

### 1. Source Generation is Required

**v1:** You could register handlers manually and rely on reflection fallback.

**v2:** You **must** use source generation. The reflection fallback has been removed.

```csharp
// v1 - Manual registration worked without source-gen
services.AddTransient<IRequestHandler<MyRequest, MyResponse>, MyHandler>();
services.AddMediatorLite();

// v2 - Must call AddGeneratedHandlers(); it registers IMediator itself
services.AddGeneratedHandlers();  // Required!
services.AddMediatorLite();       // Optional — diagnostic fallback only (see below)
```

`AddMediatorLite()` is now **optional**: the generated `AddGeneratedHandlers()` registers `IMediator` directly (scoped lifetime — it captures the resolving scope's `IServiceProvider` so scoped handler dependencies work; when resolved from the root provider it behaves like a singleton). `AddMediatorLite()` only registers a diagnostic fallback (`TryAddScoped<IMediator, ThrowingMediator>`) that throws a clear `InvalidOperationException` if the source generator never ran. The calling order of the two methods doesn't matter.

### 2. `SendAsync` / `PublishAsync` Return `ValueTask`

**v1:** `IMediator.SendAsync<TResponse>` returned `Task<TResponse>`; `IMediator.PublishAsync` returned `Task`.

**v2:** `SendAsync<TResponse>` returns `ValueTask<TResponse>` and `PublishAsync` returns `ValueTask`. This enables zero-allocation dispatch: for a request with no behaviors, a synchronously completing handler allocates nothing at all.

Plain `await` works exactly as before:

```csharp
var user = await _mediator.SendAsync(new GetUserQuery(id), ct);
await _mediator.PublishAsync(new UserCreatedNotification(user.Id), ct);
```

> ⚠️ **A `ValueTask` must be consumed exactly once** — `await` it directly. For `Task.WhenAll`, fan-out, storing the result, or awaiting more than once, convert it first with `.AsTask()`:

```csharp
var userTask = _mediator.SendAsync(new GetUserQuery(1), ct).AsTask();
var orderTask = _mediator.SendAsync(new GetOrderQuery(1), ct).AsTask();
await Task.WhenAll(userTask, orderTask);
```

### 3. Handler Registration Order

**v1:** Handlers were discovered at runtime in arbitrary order.

**v2:** Handlers are discovered at compile-time. Use `[BehaviorOrder]` to control execution order.

### 4. `ISourceGeneratedMediator`, `RequestDispatcher`, and `NotificationPublisher` Are Deleted

**v2** has no runtime dispatch layer. The source generator emits a `SourceGeneratedMediator` class (namespace `MediatorLite.Generated`) that implements `IMediator` directly. Dispatch is a compile-time type-pattern switch — no `Dictionary<Type,...>` lookup, no `Task<object>` boxing, and no runtime `Mediator` wrapper class (that class is deleted too).

If you don't call `AddGeneratedHandlers()`, the `ThrowingMediator` fallback registered by `AddMediatorLite()` throws on every dispatch:

```
InvalidOperationException: No source-generated mediator is registered. Reference the
MediatorLite.SourceGeneration analyzer package from the assembly that contains your
handlers and call services.AddGeneratedHandlers() so the generated mediator replaces
this fallback.
```

### 5. Notification Dispatch Matches the Runtime Type

**v1:** `PublishAsync` dispatched on `typeof(TNotification)`, so publishing through a base- or interface-typed reference (e.g. a variable typed as `INotification`) silently no-oped.

**v2:** The generated type-pattern switch matches the notification's **runtime type**, so base/interface-typed publishes now correctly dispatch to the concrete notification's handlers.

### 6. Runtime Options Are Gone

**v1:** `AddMediatorLite` accepted a configure lambda that exposed a `MediatorOptions` instance (logging toggles, tracing toggles, lifetimes, open-behavior list, notification strategies).

**v2:** The entire options surface has moved to compile time:

- `MediatorOptions` is **deleted**.
- `AddMediatorLite` **no longer accepts a configure lambda** — it is now a zero-argument call.
- `EnableBuiltInLogging` is replaced by the absence/presence of `[assembly: DisableMediatorLogging]`.
- `EnableTracing` is replaced by the absence/presence of `[assembly: DisableMediatorTracing]`.
- `DefaultLogLevel` is dropped; use `ILoggingBuilder.AddFilter("MediatorLite.IMediator", LogLevel.X)` or `appsettings.json`.
- `HandlerLifetime` / `MediatorLifetime` are dropped; the mediator is always `Transient`, and handler lifetimes remain controlled by the consumer at DI registration.
- `MediatorLoggingAttribute` (the per-class `Enabled` / `IncludePayload` / `LogLevel` attribute) has been **deleted** — it was never consumed.
- Notification execution/error strategies moved from `MediatorOptions` to the compile-time `[NotificationExecution]` / `[NotificationError]` (and their `[assembly: Default...]` counterparts).

```csharp
// v1
services.AddMediatorLite(options =>
{
    options.EnableBuiltInLogging = true;
    options.EnableTracing = true;
    options.DefaultLogLevel = LogLevel.Debug;
});

// v2
services.AddMediatorLite();
// Observability is on by default; opt out with [assembly: DisableMediatorLogging]
// / [assembly: DisableMediatorTracing]. Log level is controlled via MEL filters.
```

---

## Step-by-Step Migration

### Step 1: Ensure Source Generator Reference

Your project must reference `MediatorLite.SourceGeneration`:

```xml
<ItemGroup>
  <PackageReference Include="MediatorLite" Version="2.0.0" />
  <PackageReference Include="MediatorLite.SourceGeneration" Version="2.0.0" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

### Step 2: Update Service Registration

Replace manual handler registration with generated registration:

```csharp
// Before (v1)
services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserHandler>();
services.AddTransient<IRequestHandler<CreateUserCommand, Unit>, CreateUserHandler>();
services.AddMediatorLite();

// After (v2)
services.AddGeneratedHandlers();  // Discovers all handlers at compile-time
services.AddMediatorLite();
```

For granular control, use the individual methods:

```csharp
services.AddGeneratedRequestHandlers();     // Request handlers only
services.AddGeneratedNotificationHandlers(); // Notification handlers only
services.AddGeneratedBehaviors();           // Pipeline behaviors only
```

### Step 3: Update Behavior Registration

**v1:** Open generic behaviors were registered manually.

**v2:** Behaviors are auto-discovered by the source generator — no options wiring required:

```csharp
// v1 - Manual open behavior registration
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddMediatorLite();

// v2 - Behaviors are discovered automatically by the generator
services.AddGeneratedHandlers();
services.AddMediatorLite();
```

Control ordering with `[BehaviorOrder]` on the behavior class (see below).

### Step 4: Use New Attributes

v2 provides attributes for compile-time configuration:

#### `[NotificationExecution]` / `[NotificationError]` - Per-Notification Strategies

```csharp
[NotificationExecution(NotificationExecutionStrategy.Parallel)]
[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
public record UserCreatedNotification(Guid UserId) : INotification;
```

Each attribute is independent. Omit one to fall back to the assembly-level default (or the library default if none is declared).

#### `[assembly: DefaultNotificationExecution]` / `[assembly: DefaultNotificationError]` - Assembly-Wide Defaults

```csharp
[assembly: DefaultNotificationExecution(NotificationExecutionStrategy.Parallel)]
[assembly: DefaultNotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
```

Per-notification attributes always win over these assembly-level defaults.

#### `[NotificationHandlerOrder]` - Handler Execution Order

```csharp
[NotificationHandlerOrder(1)]  // Runs first
public class AuditHandler : INotificationHandler<UserCreated> { }

[NotificationHandlerOrder(2)]  // Runs second
public class EmailHandler : INotificationHandler<UserCreated> { }
```

#### `[BehaviorOrder]` - Pipeline Behavior Order

```csharp
[BehaviorOrder(1)]  // Runs first (outermost)
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }

[BehaviorOrder(2)]  // Runs second (inner)
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }
```

#### `[MediatorGeneration(Skip = true)]` - Obsolete and Inert

The attribute type is retained for binary compatibility, but the v2 generator **ignores
it**: discovery is unconditional, so a handler marked `Skip = true` is registered like any
other. If you relied on `Skip` to keep a handler out of registration, move that handler to
an assembly the source generator does not run on (for example, a test-support project).

### Step 5: Verify Generated Code

After building, check the generated code in your IDE's Analyzers section or in:
- `obj/Debug/net10.0/generated/MediatorLite.SourceGeneration/`

Look for:
- `MediatorLiteRegistration.g.cs` - Registration extension methods
- `SourceGeneratedMediator.g.cs` - Dispatch implementation

You can also access diagnostic properties:

```csharp
var requestCount = MediatorLiteRegistration.RequestHandlerCount;
var notificationCount = MediatorLiteRegistration.NotificationHandlerCount;
var behaviorCount = MediatorLiteRegistration.BehaviorCount;
```

---

## Configuration in v2

All runtime configuration options have been **removed**. `AddMediatorLite()` now takes no arguments:

```csharp
services
    .AddGeneratedHandlers()
    .AddMediatorLite();
```

The entire configuration surface has moved to compile time:

| v1 runtime option | v2 replacement |
|-------------------|----------------|
| `MediatorOptions` class | **Deleted** — `AddMediatorLite()` takes no lambda |
| `EnableBuiltInLogging` | Presence/absence of `[assembly: DisableMediatorLogging]` |
| `EnableTracing` | Presence/absence of `[assembly: DisableMediatorTracing]` |
| `DefaultLogLevel` | `ILoggingBuilder.AddFilter("MediatorLite.IMediator", LogLevel.X)` (or `appsettings.json`) |
| `HandlerLifetime` / `MediatorLifetime` | Mediator is always `Transient`; handler lifetimes remain controlled by you at DI registration |
| `AddOpenBehavior(...)` | Behaviors are auto-discovered by the source generator |
| `NotificationExecutionStrategy` / `NotificationErrorStrategy` | `[NotificationExecution]` / `[NotificationError]` per type, or `[assembly: DefaultNotificationExecution]` / `[assembly: DefaultNotificationError]` |
| `MediatorLoggingAttribute` | **Deleted** — it was never consumed |

Example opt-out of observability:

```csharp
// Any .cs file in the consuming assembly:
[assembly: DisableMediatorLogging]
[assembly: DisableMediatorTracing]
```

Example log-level configuration:

```csharp
services.AddLogging(b => b.AddFilter("MediatorLite.IMediator", LogLevel.Information));
```

---

## Common Migration Issues

### Issue: "No handler registered" Exception

**Cause:** `AddGeneratedHandlers()` not called.

**Fix:** Ensure you call `AddGeneratedHandlers()` before `AddMediatorLite()`.

### Issue: Handlers Not Discovered

**Cause:** Handler not implementing the correct interface, or the handler is generic /
nested inside a generic type (surfaced as warning `MEDL1004`).

**Fix:**
1. Verify the handler implements `IRequestHandler<TRequest, TResponse>`
2. Make the handler a non-generic class that is not nested inside a generic type

### Issue: Behaviors Executing in Wrong Order

**Cause:** v1 used registration order; v2 uses `[BehaviorOrder]` attribute.

**Fix:** Add `[BehaviorOrder(n)]` attributes to control execution order.

### Issue: Build Errors in Generated Code

**Cause:** Namespace or type conflicts.

**Fix:** 
1. Ensure all handler types are public
2. Check for duplicate handler registrations
3. Clean and rebuild the solution

---

## Performance Improvements in v2

| Metric | v1 | v2 |
|--------|----|----|
| Handler lookup | O(1) cached reflection | **O(1) static dictionary** |
| Pipeline wrapping | Runtime delegate chain | **Compile-time unrolled** |
| Memory per request | Delegate allocations | **Zero allocations** |
| Startup time | Reflection scanning | **Immediate** |

---

## Validation → FluentValidation

The in-house validation model was **removed**. `MediatorLite.Validation.IValidator<T>`,
`DataAnnotationsValidator<T>`, and `ValidationResult` no longer exist. Validation is now powered
by **FluentValidation** via the opt-in **`MediatorLite.FluentValidation`** package.

- Add the package reference: `MediatorLite.FluentValidation` (brings in `FluentValidation`).
  If the generator finds validators for handled requests but the package is missing, the build
  fails with **`MEDL1001`**.
- Replace `IValidator<T>` implementations with FluentValidation `AbstractValidator<T>` (`RuleFor(...)`).
- Replace DataAnnotations (`[Required]`, `[Range]`, `[EmailAddress]`, …) with the equivalent
  FluentValidation rules (`NotEmpty()`, `InclusiveBetween(...)`, `EmailAddress()`, …).
- `ValidationException` and `ValidationError` are **unchanged** — FluentValidation failures are
  mapped onto them, so existing `catch (ValidationException)` blocks keep working.
- Registration is still automatic via `AddGeneratedHandlers()`; never call
  `AddValidatorsFromAssembly(...)`. The generator emits `FluentValidationBehavior<,>` as the
  outermost pipeline behavior. See [validation.md](validation.md).

## Behavior Change: Cancellation Under ContinueAndAggregate (v2.x)

A handler-thrown `OperationCanceledException` is now treated as an ordinary fault when
the **publish** `CancellationToken` is not cancelled:

- **Parallel + ContinueAndAggregate**: previously the first OCE was rethrown unwrapped
  and any sibling handler faults were silently dropped. Now all faults — OCEs included —
  arrive inside one `AggregateException`.
- **Sequential / StopOnFirst + ContinueAndAggregate**: previously any handler OCE aborted
  the publish and skipped the remaining handlers. Now remaining handlers keep running and
  the OCE is aggregated.

Genuine cancellation (the token you passed to `PublishAsync` is cancelled) still surfaces
as an unwrapped `OperationCanceledException`. If you catch `OperationCanceledException`
around `PublishAsync` to detect handler-internal cancellations, inspect
`AggregateException.InnerExceptions` instead.

## Summary

1. **Add source generator** package reference
2. **Replace manual registration** with `AddGeneratedHandlers()`
3. **Use attributes** for ordering and configuration
4. **Remove reflection fallback code** (it no longer exists)
5. **Build and verify** generated code is correct

The v2 architecture is simpler, faster, and eliminates ~500 lines of reflection complexity.
