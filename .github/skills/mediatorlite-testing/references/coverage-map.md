# Coverage Map

## Test Counts

- **~122 test methods** across **14 files** (13 test files + 1 shared type file)
- Reflection: 6 files, ~49 tests
- SourceGeneration: 5 files (4 test + 1 types), ~44 tests
- UnitTests: 3 files, ~29 tests

## Feature Coverage Matrix

| Feature | Reflection File | SourceGeneration File | UnitTests File |
|---|---|---|---|
| **Request dispatch** (valid request → response) | `Reflection/MediatorTests.cs` | `SourceGeneration/MediatorTests.cs` | — |
| **Void requests** (`IRequest` → `Unit`) | `Reflection/MediatorTests.cs` | `SourceGeneration/MediatorTests.cs` | — |
| **Missing handler** → `InvalidOperationException` | `Reflection/MediatorTests.cs` | — | — |
| **Null request** → `ArgumentNullException` | `Reflection/MediatorTests.cs` | `SourceGeneration/MediatorTests.cs` | — |
| **Exception propagation** from handler | `Reflection/MediatorTests.cs` | `SourceGeneration/MediatorTests.cs` | — |
| **Cancellation token** support | `Reflection/MediatorTests.cs` | `SourceGeneration/MediatorTests.cs` | — |
| **Unit type** (singleton, CompareTo, ToString, CompletedTask) | `Reflection/MediatorTests.cs` (UnitTests class) | — | — |
| **Pipeline behaviors — registration order** | `Reflection/PipelineBehaviorTests.cs` | `SourceGeneration/PipelineBehaviorTests.cs` | — |
| **Pipeline behaviors — open generic** | `Reflection/PipelineBehaviorTests.cs` | `SourceGeneration/PipelineBehaviorTests.cs` | — |
| **Pipeline behaviors — short-circuit** (not calling `next()`) | `Reflection/PipelineBehaviorTests.cs` | `SourceGeneration/PipelineBehaviorTests.cs` | — |
| **Pipeline behaviors — multiple behaviors** | — | `SourceGeneration/PipelineBehaviorTests.cs` | — |
| **Pipeline behaviors — exception propagation through behavior** | — | `SourceGeneration/PipelineBehaviorTests.cs` | — |
| **Pipeline behaviors — AddOpenBehavior via options** | — | `SourceGeneration/PipelineBehaviorTests.cs` | — |
| **Pipeline behaviors — no behaviors (direct dispatch)** | — | `SourceGeneration/PipelineBehaviorTests.cs` | — |
| **Pipeline behaviors — source-gen inner handler dispatch** | — | `SourceGeneration/PipelineBehaviorTests.cs` | — |
| **Notifications — all handlers invoked** | `Reflection/NotificationTests.cs` | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — handler ordering** (`[NotificationHandlerOrder]`) | `Reflection/NotificationTests.cs` | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — Sequential strategy** | `Reflection/NotificationTests.cs` | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — Parallel strategy** | `Reflection/NotificationTests.cs` | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — StopOnFirst strategy** | `Reflection/NotificationTests.cs` | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — StopOnFirst + ContinueAndAggregate (fallback)** | `Reflection/NotificationTests.cs` | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — StopOnFirst + StopOnFirstError** | `Reflection/NotificationTests.cs` | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — all handlers fail → AggregateException** | `Reflection/NotificationTests.cs` | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — Parallel ignores StopOnFirstError** | `Reflection/NotificationTests.cs` | — | — |
| **Notifications — per-notification `[NotificationOptions]`** | `Reflection/NotificationTests.cs` | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — global strategy override by attribute** | — | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — no handlers completes without error** | `Reflection/NotificationTests.cs` | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — cancellation** | — | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — with tracing enabled** | — | `SourceGeneration/NotificationTests.cs` | — |
| **Notifications — with logging enabled** | — | `SourceGeneration/NotificationTests.cs` | — |
| **Validation — DataAnnotations valid request passes** | `Reflection/ValidationTests.cs` | `SourceGeneration/ValidationTests.cs` | — |
| **Validation — DataAnnotations invalid request throws** | `Reflection/ValidationTests.cs` | `SourceGeneration/ValidationTests.cs` | — |
| **Validation — custom `IValidator<T>`** | `Reflection/ValidationTests.cs` | `SourceGeneration/ValidationTests.cs` | — |
| **Validation — short-circuit before handler** | — | `SourceGeneration/ValidationTests.cs` | — |
| **Validation — short-circuit before other behaviors** | — | `SourceGeneration/ValidationTests.cs` | — |
| **Validation — execution order (validation first)** | — | `SourceGeneration/ValidationTests.cs` | — |
| **Validation — multiple errors aggregated** | — | `SourceGeneration/ValidationTests.cs` | — |
| **Validation — auto-registration of DataAnnotationsValidator** | — | `SourceGeneration/ValidationTests.cs` | — |
| **Validation — auto-registration of custom validator** | — | `SourceGeneration/ValidationTests.cs` | — |
| **Validation — ValidatorCount reported by source gen** | — | `SourceGeneration/ValidationTests.cs` | — |
| **ValidationResult** (Success, Failure, IsValid, Errors) | `Reflection/ValidationTests.cs` | — | `UnitTests/ValidationTests.cs` |
| **ValidationException** (message format, Errors property) | `Reflection/ValidationTests.cs` | — | `UnitTests/ValidationTests.cs` |
| **ValidationError** (record, with-expression) | — | — | `UnitTests/ValidationTests.cs` |
| **MediatorOptions — default values** | `Reflection/MediatorOptionsTests.cs` | — | — |
| **MediatorOptions — property setters** | `Reflection/MediatorOptionsTests.cs` | — | — |
| **MediatorOptions — AddOpenBehavior validation** (null, non-generic, non-behavior) | `Reflection/MediatorOptionsTests.cs` | — | — |
| **MediatorOptions — AddBehavior** | `Reflection/MediatorOptionsTests.cs` | — | — |
| **MediatorOptions — chained calls** | `Reflection/MediatorOptionsTests.cs` | — | — |
| **DI registration — AddMediatorLite without config** | `Reflection/ServiceCollectionExtensionsTests.cs` | — | — |
| **DI registration — AddMediatorLite with options** | `Reflection/ServiceCollectionExtensionsTests.cs` | — | — |
| **DI registration — AddMediatorBehavior** | `Reflection/ServiceCollectionExtensionsTests.cs` | — | — |
| **DI registration — MediatorLifetime Transient vs Singleton** | `Reflection/ServiceCollectionExtensionsTests.cs` | — | — |
| **DI registration — HandlerLifetime Scoped** | `Reflection/ServiceCollectionExtensionsTests.cs` | — | — |
| **DI registration — multiple behaviors** | `Reflection/ServiceCollectionExtensionsTests.cs` | — | — |
| **Source-gen — AddGeneratedHandlers registers ISourceGeneratedMediator** | — | `SourceGeneration/MediatorTests.cs` | — |
| **Source-gen — RequestHandlerCount** | — | `SourceGeneration/MediatorTests.cs` | — |
| **Source-gen — NotificationHandlerCount** | — | `SourceGeneration/MediatorTests.cs` | — |
| **Source-gen — TrySendAsync dispatch** | — | `SourceGeneration/MediatorTests.cs` | — |
| **Source-gen — TryInvokeHandlerAsync** | — | `SourceGeneration/PipelineBehaviorTests.cs` | — |
| **Source-gen — TryResolveBehaviors** | — | `SourceGeneration/PipelineBehaviorTests.cs` | — |
| **Source-gen — TryGetHandlerOrder** | — | `SourceGeneration/NotificationTests.cs` | — |
| **Source-gen — TryGetNotificationOptions** | — | `SourceGeneration/NotificationTests.cs` | — |
| **Source-gen — multiple request types dispatched** | — | `SourceGeneration/MediatorTests.cs` | — |
| **Source-gen — tracing enabled** | — | `SourceGeneration/MediatorTests.cs` | — |
| **Source-gen — logging enabled** | — | `SourceGeneration/MediatorTests.cs` | — |
| **Attributes — NotificationHandlerOrderAttribute** | — | — | `UnitTests/AttributeTests.cs` |
| **Attributes — NotificationOptionsAttribute** | — | — | `UnitTests/AttributeTests.cs` |
| **Attributes — BehaviorOrderAttribute** | — | — | `UnitTests/AttributeTests.cs` |
| **Attributes — MediatorLoggingAttribute** | — | — | `UnitTests/AttributeTests.cs` |
| **Attributes — MediatorGenerationAttribute** | — | — | `UnitTests/AttributeTests.cs` |
| **DTO records** (construction, deconstruction, with-expression, equality, GetHashCode, ToString) | — | — | `UnitTests/DtoPropertySettersTests.cs` |

## Known Gaps (Not Currently Covered)

| Feature | Notes |
|---|---|
| `[MediatorLogging]` per-request behavior | Attribute exists and is tested in `AttributeTests` but no integration test verifies it changes logging behavior |
| Scoped lifetime resolution depth | `HandlerLifetime.Scoped` is set in `ServiceCollectionExtensionsTests` but no test verifies scope-per-request behavior |
| `BehaviorOrderAttribute` actual ordering | Attribute is tested for construction but mediator relies on DI registration order, not the attribute |
| Concurrency / thread-safety | No tests exercise concurrent `SendAsync` or `PublishAsync` calls |
| `MediatorLifetime.Scoped` | Transient and Singleton are tested, Scoped is not |
| Missing handler in source-gen path | Reflection tests cover this; source-gen tests assume all handlers are discovered |
| Open generic behavior via `AddOpenBehavior` with Reflection path | Covered only with direct DI registration (`typeof(IPipelineBehavior<,>)`) |
| ActivitySource / tracing tag verification | Tests verify no-throw with tracing enabled but don't inspect emitted spans or tags |
| Built-in logging output verification | Tests verify no-throw with logging enabled but don't inspect log output |

## File Index

| File | Tests | Category |
|---|---|---|
| `Reflection/MediatorTests.cs` | 10 | Request dispatch, void requests, errors, cancellation, Unit type |
| `Reflection/MediatorOptionsTests.cs` | 12 | Options defaults, setters, AddOpenBehavior validation |
| `Reflection/NotificationTests.cs` | 10 | All notification strategies and error handling |
| `Reflection/PipelineBehaviorTests.cs` | 3 | Behavior order, open generic, short-circuit |
| `Reflection/ServiceCollectionExtensionsTests.cs` | 8 | DI registration, lifetimes, behaviors |
| `Reflection/ValidationTests.cs` | 6 | DataAnnotations, custom validator, ValidationResult/Exception |
| `SourceGeneration/TestTypes.cs` | 0 | Shared type definitions (handlers, notifications, behaviors, validators) |
| `SourceGeneration/MediatorTests.cs` | 12 | Source-gen dispatch, counts, tracing, logging |
| `SourceGeneration/NotificationTests.cs` | 14 | Notifications with source-gen, handler order, per-notification options |
| `SourceGeneration/PipelineBehaviorTests.cs` | 9 | Behaviors with source-gen dispatch, TryResolveBehaviors |
| `SourceGeneration/ValidationTests.cs` | 9 | Source-gen validation auto-registration, execution order, short-circuit |
| `UnitTests/AttributeTests.cs` | 5 | All attribute construction and property tests |
| `UnitTests/DtoPropertySettersTests.cs` | 17 | Record construction, deconstruction, equality, hashing |
| `UnitTests/ValidationTests.cs` | 7 | ValidationError, ValidationResult, ValidationException unit tests |
