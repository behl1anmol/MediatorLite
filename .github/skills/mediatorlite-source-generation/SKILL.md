---
name: mediatorlite-source-generation
description: Knowledge skill for the MediatorLite source generator project (src/MediatorLite.SourceGeneration). Use this skill whenever working on or modifying the HandlerDiscoveryGenerator, debugging source generator output, extending handler/behavior/validator discovery, understanding generated dispatch code (MediatorLiteRegistration.g.cs and SourceGeneratedMediator.g.cs), or improving compile-time code generation. Also consult when adding new discoverable types or fixing source-gen related issues.
---

# MediatorLite Source Generation

## Project Overview

The source generator lives in `src/MediatorLite.SourceGeneration/` and consists of two source files:

- **HandlerDiscoveryGenerator.cs** — The single incremental source generator (~1150 lines). Implements `IIncrementalGenerator` and is decorated with `[Generator(LanguageNames.CSharp)]`.
- **IsExternalInit.cs** — Polyfill for `init`-only properties and records under netstandard2.0.

### Build & Packaging Constraints

- **Target framework**: `netstandard2.0` (required by Roslyn hosting — analyzers/generators must target this).
- **Roslyn dependency**: `Microsoft.CodeAnalysis.CSharp` 4.8.0 (private asset).
- **Analyzer rules**: `EnforceExtendedAnalyzerRules=true`, `IsRoslynComponent=true`.
- **Packaging**: The compiled DLL is placed in `analyzers/dotnet/cs` via `<None Include="$(OutputPath)\$(AssemblyName).dll" Pack="true" PackagePath="analyzers/dotnet/cs" />`. The package is marked `DevelopmentDependency=true` and `IncludeBuildOutput=false` — consumers get the analyzer DLL only, no lib reference.
- **No compile-time dependency on MediatorLite core**: The generator references MediatorLite interfaces by string name only (e.g. `"MediatorLite.IRequestHandler<TRequest, TResponse>"`). The shared contract is `ISourceGeneratedMediator` in the core lib.

## Generator Architecture

`HandlerDiscoveryGenerator.Initialize()` sets up **four parallel `CreateSyntaxProvider` pipelines**, each with a syntactic predicate and a semantic transform:

| Pipeline | Predicate | Transform | Discovers |
|---|---|---|---|
| Handlers | `IsHandlerCandidate` — non-abstract `ClassDeclarationSyntax` with a `BaseList` | `GetHandlerInfo` | `IRequestHandler<TReq,TRes>` and `INotificationHandler<T>` implementations |
| Notifications | `IsNotificationCandidate` — `TypeDeclarationSyntax` with a `BaseList` | `GetNotificationInfo` | `INotification` types decorated with `[NotificationOptions]` |
| Behaviors | `IsBehaviorCandidate` — non-abstract `ClassDeclarationSyntax` with a `BaseList` | `GetBehaviorInfo` | `IPipelineBehavior<TReq,TRes>` implementations (open or closed generic) |
| Validators | `IsValidatorCandidate` — non-abstract `ClassDeclarationSyntax` with a `BaseList` | `GetValidatorInfo` | `IValidator<TRequest>` implementations (concrete only, open-generic skipped) |

All four pipelines' results are `.Collect()`-ed, combined with `CompilationProvider`, and fed into a single `RegisterSourceOutput` which calls `Execute(...)`.

## Discovery Summary

### Handlers (`GetHandlerInfo`)
- Scans `classSymbol.AllInterfaces` for `MediatorLite.IRequestHandler<TRequest, TResponse>` and `MediatorLite.INotificationHandler<TNotification>`.
- For request handlers: captures `RequestType`, `ResponseType`, and `HasDataAnnotations` (via `HasDataAnnotationAttributes` which walks properties for `System.ComponentModel.DataAnnotations.ValidationAttribute` inheritance).
- For notification handlers: captures `NotificationType` and `Order` (from `[NotificationHandlerOrderAttribute]` constructor argument).
- Skips types with `[MediatorGeneration(Skip = true)]`.

### Notifications (`GetNotificationInfo`)
- Finds types implementing `MediatorLite.INotification` that carry `[NotificationOptionsAttribute]`.
- Reads `ExecutionStrategy`, `ErrorStrategy`, and `OverrideGlobal` from named arguments (defaults: 0, 1, true).
- Returns `null` if `OverrideGlobal` is false.

### Behaviors (`GetBehaviorInfo`)
- Scans for `IPipelineBehavior<TRequest, TResponse>`.
- Detects `IsOpenGeneric` — class has type parameters and interface type arguments are `TypeKind.TypeParameter`.
- Skips types with `[MediatorGeneration(Skip = true)]`.

### Validators (`GetValidatorInfo`)
- Scans for `MediatorLite.Validation.IValidator<TRequest>`.
- **Skips open-generic validators** (e.g. `DataAnnotationsValidator<T>` from the core library).
- Only discovers validators for concrete (non-generic) request types.
- Skips types with `[MediatorGeneration(Skip = true)]`.

### Common Exclusion
All four pipelines check for `[MediatorGenerationAttribute]` with `Skip = true` and exclude matching types from discovery.

## Generated Output

The `Execute` method produces **two generated files** via `context.AddSource(...)`:

### 1. MediatorLiteRegistration.g.cs

Static class `MediatorLite.Generated.MediatorLiteRegistration` with extension methods on `IServiceCollection`:

| Method | Registers |
|---|---|
| `AddGeneratedHandlers()` | Calls all granular methods below + registers `SourceGeneratedMediator` as singleton `ISourceGeneratedMediator` |
| `AddGeneratedRequestHandlers()` | Each request handler as `Transient<IRequestHandler<TReq,TRes>, ConcreteHandler>` |
| `AddGeneratedNotificationHandlers()` | Each notification handler as `Transient<INotificationHandler<T>, ConcreteHandler>` |
| `AddGeneratedValidators()` | Custom validators + auto-generated `DataAnnotationsValidator<T>` registrations for request types with DataAnnotation attributes |
| `AddGeneratedBehaviors()` | ValidationBehavior registrations **first**, then other expanded behaviors (all transient) |

Static diagnostic properties: `RequestHandlerCount`, `NotificationHandlerCount`, `BehaviorCount`, `ValidatorCount`.

When no handlers are found, `GenerateEmptyRegistration` produces a minimal class with all methods returning `services` unchanged and counts set to 0.

### 2. SourceGeneratedMediator.g.cs

Sealed class `MediatorLite.Generated.SourceGeneratedMediator` implementing `ISourceGeneratedMediator`:

- **`TrySendAsync<TResponse>`** — `request switch { ConcreteRequest r => DispatchAs<TResponse, TActualRes>(...), _ => null }` pattern.
- **`TryInvokeHandlerAsync<TResponse>`** — Same switch pattern for inner handler invocation when behaviors are present.
- **`TryGetHandlerOrder`** — Dictionary lookup in `_handlerOrderMap` (handler FQN → order int). Returns null when no ordered handlers.
- **`TryGetNotificationOptions`** — Dictionary lookup in `_notificationOptionsMap` (notification FQN → `(ExecutionStrategy, ErrorStrategy)` tuple).
- **`TryGetCachedHandlers<T>`** — Type switch resolving concrete notification handler instances via `GetRequiredService<ConcreteHandler>()`, returning `List<INotificationHandler<T>>`.
- **`TryResolveBehaviors`** — `(requestType, responseType) switch` dispatching to typed helper methods `ResolveBehaviorsFor_{SafeName}(sp)` that call `GetServices<IPipelineBehavior<TReq,TRes>>()`.
- **`InvokeHandler<TResponse>`** — `(requestType, typeof(TResponse)) switch` with tuple pattern matching, casting handler/request to concrete types.
- **`InvokeBehavior<TResponse>`** — `(requestType, typeof(TResponse), behaviorType) switch` with triple-tuple pattern matching for each expanded behavior.
- **`DispatchAs<TResponse, TActual>`** — Helper to await and box-cast `ValueTask<TActual>` to `ValueTask<TResponse>`.

## Key Patterns

- **Open-generic expansion at compile time**: `ExpandBehaviors()` closes open-generic behaviors (e.g. `LoggingBehavior<,>`) over every discovered request/response pair, producing `ExpandedBehaviorInfo` entries.
- **`global::` FQN in generated code**: All type names use `SymbolDisplayFormat.FullyQualifiedFormat` which produces `global::Namespace.Type` to avoid ambiguity.
- **`GetSafeTypeName` conversion**: Strips `global::`, replaces `.`, `<`, `>`, `,`, and space with `_` — used for per-request helper method names (e.g. `ResolveBehaviorsFor_MyApp_Commands_CreateOrderCommand`).
- **Try-pattern with nullable returns**: All `Try*` methods return nullable types; `null` signals the caller to fall back to reflection-based dispatch in the core mediator.
- **No Roslyn diagnostics emitted**: The generator does not report any `DiagnosticDescriptor`s — it silently skips unrecognized types.
- **Validation-first ordering**: `ValidationBehavior` registrations are emitted before other behaviors in `AddGeneratedBehaviors()` to ensure validation executes first in the pipeline.
- **DataAnnotation auto-detection**: `HasDataAnnotationAttributes` walks property attributes checking for `ValidationAttribute` base class inheritance, enabling automatic `DataAnnotationsValidator<T>` registration without explicit validator classes.

## Critical Rules

1. **No compile-time dependency on the core library** — The generator references MediatorLite types by their fully-qualified string names only. The contract is the `ISourceGeneratedMediator` interface in the core lib (`src/MediatorLite/Abstractions/ISourceGeneratedMediator.cs`).
2. **Must target netstandard2.0** — Roslyn requires analyzer/generator assemblies to be netstandard2.0. The `IsExternalInit` polyfill enables record syntax.
3. **DevelopmentDependency packaging** — `DevelopmentDependency=true`, `IncludeBuildOutput=false`, DLL packed into `analyzers/dotnet/cs`. Consumer projects reference with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`.
4. **Incremental generator API** — Uses `IIncrementalGenerator` (not the older `ISourceGenerator`) for performance. Syntactic predicates must be fast and static.
5. **All registrations are transient** — Generated DI registrations use `AddTransient` except `ISourceGeneratedMediator` which is singleton.

## References

See the `references/` directory for deep-dives:

- [references/generator-internals.md](references/generator-internals.md) — Detailed breakdown of each `CreateSyntaxProvider` pipeline, transform methods, data records, `ExpandBehaviors`, and `DetermineValidationTargets`.
- [references/generated-code.md](references/generated-code.md) — Structure and patterns of both generated files (`MediatorLiteRegistration.g.cs` and `SourceGeneratedMediator.g.cs`).
- [references/extending.md](references/extending.md) — How to add new discoverable types, `GetSafeTypeName` details, integration contract, consumer project setup, and packaging.
