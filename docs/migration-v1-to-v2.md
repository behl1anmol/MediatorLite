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
| Handler resolution | `ConcurrentDictionary` caching | **Static `Dictionary<Type, Delegate>`** |
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

// v2 - Must call AddGeneratedHandlers()
services.AddGeneratedHandlers();  // Required!
services.AddMediatorLite();
```

### 2. Handler Registration Order

**v1:** Handlers were discovered at runtime in arbitrary order.

**v2:** Handlers are discovered at compile-time. Use `[BehaviorOrder]` to control execution order.

### 3. `ISourceGeneratedMediator` is Required

**v2** dispatches through `ISourceGeneratedMediator`. If you don't call `AddGeneratedHandlers()`, you'll get:

```
InvalidOperationException: No handler registered for request type 'MyRequest'
```

### 4. Runtime Options Are Gone

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

#### `[MediatorGeneration(Skip = true)]` - Obsolete

This attribute is obsolete in v2 and is retained only for legacy compatibility. Avoid using it for new code.

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

**Cause:** Handler marked with `[MediatorGeneration(Skip = true)]` or not implementing correct interface.

**Fix:** 
1. Remove `[MediatorGeneration(Skip = true)]` or register manually
2. Verify handler implements `IRequestHandler<TRequest, TResponse>`

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

## Summary

1. **Add source generator** package reference
2. **Replace manual registration** with `AddGeneratedHandlers()`
3. **Use attributes** for ordering and configuration
4. **Remove reflection fallback code** (it no longer exists)
5. **Build and verify** generated code is correct

The v2 architecture is simpler, faster, and eliminates ~500 lines of reflection complexity.
