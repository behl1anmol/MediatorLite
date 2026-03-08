# Conventions Reference

Coding conventions, design principles, and build configuration for the MediatorLite core library.

---

## Namespace Conventions

| Namespace | Location | Contents |
|-----------|----------|----------|
| `MediatorLite` | `Abstractions/`, root | All public interfaces, marker types, attributes, enums, DI extensions — the consumer-facing API surface |
| `MediatorLite.Configuration` | `Configuration/` | `MediatorOptions` — configuration-only types |
| `MediatorLite.Diagnostics` | `Diagnostics/` | `MediatorActivitySource`, `MediatorDiagnostics` — observability infrastructure |
| `MediatorLite.Internal` | `Internal/` | `Mediator` — the sealed internal dispatcher; not part of the public API |
| `MediatorLite.Validation` | `Validation/` | `IValidator<T>`, `DataAnnotationsValidator<T>`, `ValidationBehavior<,>`, `ValidationException` |
| `MediatorLite.Validation.Models` | `Validation/Models/` | `ValidationResult`, `ValidationError` — pure data models |

Design principle: the root `MediatorLite` namespace contains everything a consumer needs with a single `using MediatorLite;`. Configuration, diagnostics, internal implementation, and validation are separated into sub-namespaces.

---

## ValueTask vs Task

### Rule

- **Handler interfaces** (`IRequestHandler`, `INotificationHandler`, `IPipelineBehavior`, `IValidator`) return `ValueTask<T>` or `ValueTask`.
- **Public API** (`IMediator.SendAsync`, `IMediator.PublishAsync`) returns `Task<T>` or `Task`.

### Rationale

**ValueTask for handlers:** Many handlers complete synchronously (cache hits, in-memory lookups, validation checks). `ValueTask<T>` avoids the `Task<T>` heap allocation on these hot paths. Since handlers are called internally by the mediator (never by consumer code needing `Task.WhenAll`), the `ValueTask` constraints (single-await, no concurrent access) are naturally satisfied.

**Task for public API:** Consumers of `IMediator` often want to compose multiple mediator calls:

```csharp
var userTask = mediator.SendAsync(new GetUserQuery(1), ct);
var orderTask = mediator.SendAsync(new GetOrderQuery(2), ct);
await Task.WhenAll(userTask, orderTask);
```

`Task.WhenAll` requires `Task`, not `ValueTask`. Returning `Task` from the public API provides natural ergonomics without forcing consumers to call `.AsTask()` on every call.

### Conversion Point

The `Mediator` class internally `await`s the `ValueTask` from handlers and returns the result as a `Task` via `async Task<TResponse> SendAsync(...)`. The `async` method machinery handles the `Task` wrapping.

---

## Sealed Classes

### Rule

All concrete/internal classes are `sealed`:

- `Mediator` (internal sealed)
- `MediatorOptions` (sealed)
- `ValidationBehavior<,>` (sealed)
- `ValidationException` (sealed)
- `ValidationResult` (sealed)
- `ValidationError` (sealed record)
- All attributes (sealed)

### Rationale

Sealing classes enables JIT devirtualization: the runtime knows no subclass can override virtual/interface methods, so it can inline calls. This is meaningful in the mediator's hot path where handler dispatch happens on every request.

**Exception:** `DataAnnotationsValidator<T>` is `public class` (not sealed) intentionally, allowing consumers to extend it if needed.

---

## No-Reflection Principle

### Rule

Never introduce new reflection paths in the core library. All new dispatch logic should go through `ISourceGeneratedMediator`.

### Existing Reflection

The reflection fallback in `Internal/Mediator.cs` exists only for backward compatibility with manually registered handlers not known at compile time. It uses:

- `typeof(IRequestHandler<,>).MakeGenericType(...)` — cached in `ConcurrentDictionary`
- `typeof(IPipelineBehavior<,>).MakeGenericType(...)` — cached in `ConcurrentDictionary`
- `MethodInfo.Invoke(handler, ...)` — for calling `HandleAsync` on the resolved handler
- `Type.GetCustomAttribute<T>()` — cached in `ConcurrentDictionary` for handler ordering and notification options

All these caches are `static readonly` fields on `Mediator`, ensuring cross-instance sharing and thread safety.

### Source-Gen Dispatch

When `ISourceGeneratedMediator` is registered (via `AddGeneratedHandlers()`), the mediator:
1. Tries source-gen first on every operation.
2. Only falls back to reflection when the source-gen returns `null` (type not discovered at compile time).
3. In a fully source-gen setup, zero reflection occurs at runtime.

---

## Error Handling Patterns

### Exception Types by Scenario

| Scenario | Exception Type | Location |
|----------|---------------|----------|
| No handler for request type | `InvalidOperationException` | `Mediator.SendAsync`, `Mediator.ExecutePipeline` |
| Validation failure | `MediatorLite.Validation.ValidationException` | `ValidationBehavior.HandleAsync` |
| Notification handlers fail (ContinueAndAggregate) | `AggregateException` | `Mediator.ExecuteSequential`, `ExecuteParallel`, `ExecuteStopOnFirst` |
| Notification handlers fail (StopOnFirstError) | Original exception (re-thrown) | `Mediator.ExecuteSequential`, `ExecuteStopOnFirst` |
| Cancellation during notification handling | `OperationCanceledException` | Always re-thrown immediately |
| Behavior/handler type unknown to source-gen | `InvalidOperationException` | `ISourceGeneratedMediator.InvokeHandler`, `InvokeBehavior` |
| Invalid behavior type in MediatorOptions | `ArgumentException` | `MediatorOptions.AddOpenBehavior` |
| Invalid behavior type in AddMediatorBehavior | `ArgumentException` | `ServiceCollectionExtensions.AddMediatorBehavior` |

### ExceptionDispatchInfo Pattern

When invoking handlers/behaviors via reflection, `TargetInvocationException` wraps the real exception. The mediator preserves the original stack trace:

```csharp
try
{
    return (ValueTask<TResponse>)method.Invoke(handler, [request, cancellationToken])!;
}
catch (TargetInvocationException ex) when (ex.InnerException != null)
{
    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
    throw; // Unreachable — satisfies compiler
}
```

This pattern is used in both `InvokeHandlerFromDI` and `InvokeBehaviorAsync` reflection fallback paths.

### OperationCanceledException

In notification execution, `OperationCanceledException` is **always** re-thrown immediately, regardless of the configured `NotificationErrorStrategy`:

```csharp
catch (OperationCanceledException)
{
    throw;  // Never caught by ContinueAndAggregate
}
```

This ensures cancellation semantics are respected even in aggregate error mode.

---

## Build Configuration

From `Directory.Build.props` (applies to all projects in the solution):

```xml
<PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>
</PropertyGroup>
```

| Setting | Value | Impact |
|---------|-------|--------|
| `TargetFramework` | `net10.0` | .NET 10 only — no multi-targeting |
| `LangVersion` | `latest` | All latest C# features available (collection literals `[]`, primary constructors, etc.) |
| `Nullable` | `enable` | Nullable reference types enforced — all `?` annotations are meaningful |
| `ImplicitUsings` | `enable` | `System`, `System.Collections.Generic`, `System.Linq`, `System.Threading`, `System.Threading.Tasks` automatically imported |
| `TreatWarningsAsErrors` | `true` | Zero-warning policy — all warnings are build errors |
| `EnforceCodeStyleInBuild` | `true` | Code style rules enforced at build time |
| `EnableNETAnalyzers` | `true` | .NET SDK analyzers enabled |
| `AnalysisLevel` | `latest` | Latest analyzer rules applied |

### Core Library Package Dependencies

From `src/MediatorLite/MediatorLite.csproj`:

| Package | Version |
|---------|---------|
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 9.0.0 |
| `Microsoft.Extensions.Logging.Abstractions` | 9.0.0 |

The core library only depends on abstractions (not concrete implementations), keeping the dependency footprint minimal.

---

## Code Style Patterns

### Collection Literals

Uses C# 12+ collection expression syntax:

```csharp
private readonly List<Type> _behaviorTypes = [];
public IReadOnlyList<ValidationError> Errors { get; private init; } = [];
(exceptions ??= []).Add(ex);
```

### Primary Constructors

Attributes use C# 12 primary constructor syntax:

```csharp
public sealed class NotificationHandlerOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}
```

### Null Coalescing Assignment

```csharp
(exceptions ??= []).Add(ex);  // Create list on first exception, reuse after
```

### Pattern Matching

```csharp
if (exceptions is { Count: > 0 })  // Property pattern for non-empty collection check
```

### ArgumentNullException.ThrowIfNull

Used consistently instead of manual null checks:

```csharp
ArgumentNullException.ThrowIfNull(request);
ArgumentNullException.ThrowIfNull(behaviorType);
```
