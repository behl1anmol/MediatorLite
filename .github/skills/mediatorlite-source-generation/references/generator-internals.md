# Generator Internals

Detailed breakdown of the four `CreateSyntaxProvider` pipelines, their transform methods, and supporting data records in `HandlerDiscoveryGenerator.cs`.

## Pipeline 1: Handlers

### Predicate — `IsHandlerCandidate`

```csharp
return node is ClassDeclarationSyntax classDecl
       && classDecl.BaseList is not null
       && !classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);
```

Matches any non-abstract class declaration that has a base list (i.e., implements interfaces or inherits from a base class).

### Transform — `GetHandlerInfo`

1. Gets the `INamedTypeSymbol` via `semanticModel.GetDeclaredSymbol`.
2. Checks for `[MediatorGenerationAttribute]` with `Skip = true` — returns `null` if present.
3. Checks for `[NotificationHandlerOrderAttribute]` — extracts the `int` order from the first constructor argument.
4. Iterates `classSymbol.AllInterfaces`:
   - **`MediatorLite.IRequestHandler<TRequest, TResponse>`**: Captures `RequestType`, `ResponseType` (both FQN), and `HasDataAnnotations` (calls `HasDataAnnotationAttributes` on the request type).
   - **`MediatorLite.INotificationHandler<TNotification>`**: Captures `NotificationType` (FQN) and `Order` (from the attribute).
5. Returns `null` if no matching interfaces found.

### `HasDataAnnotationAttributes` (helper)

Walks all `IPropertySymbol` members of the request type. For each property, checks if any attribute inherits from `System.ComponentModel.DataAnnotations.ValidationAttribute` by walking up the `BaseType` chain via `IsValidationAttribute`.

### Output Record — `HandlerInfo`

```
HandlerInfo(
    ClassName: string,          // global:: FQN of the handler class
    Namespace: string,          // containing namespace
    RequestHandlers: List<HandlerInterfaceInfo>,
    NotificationHandlers: List<NotificationHandlerInterfaceInfo>
)
```

### `HandlerInterfaceInfo`

```
HandlerInterfaceInfo(
    InterfaceType: string,      // e.g. global::MediatorLite.IRequestHandler<global::MyApp.CreateCommand, global::MediatorLite.Unit>
    RequestType: string,        // e.g. global::MyApp.CreateCommand
    ResponseType: string?,      // e.g. global::MediatorLite.Unit
    HasDataAnnotations: bool    // true if request type properties have validation attributes
)
```

### `NotificationHandlerInterfaceInfo`

```
NotificationHandlerInterfaceInfo(
    InterfaceType: string,      // e.g. global::MediatorLite.INotificationHandler<global::MyApp.UserCreated>
    NotificationType: string,   // e.g. global::MyApp.UserCreated
    Order: int?                 // from [NotificationHandlerOrder(N)]
)
```

---

## Pipeline 2: Notifications

### Predicate — `IsNotificationCandidate`

```csharp
return node is TypeDeclarationSyntax typeDecl
       && typeDecl.BaseList is not null;
```

Broader than handlers — matches any `TypeDeclarationSyntax` (class, struct, record) with a base list. This is because notifications can be records.

### Transform — `GetNotificationInfo`

1. Gets `INamedTypeSymbol` from the type declaration.
2. Checks if the type implements `MediatorLite.INotification` (via `AllInterfaces` string comparison).
3. Looks for `NotificationOptionsAttribute` in the type's attributes.
4. If no `[NotificationOptions]` attribute, returns `null` (only types with explicit options are captured).
5. Reads named arguments with defaults:
   - `ExecutionStrategy` → `int`, default `0` (Sequential)
   - `ErrorStrategy` → `int`, default `1` (ContinueAndAggregate)
   - `OverrideGlobal` → `bool`, default `true`
6. Returns `null` if `OverrideGlobal` is `false`.

### Output Record — `NotificationTypeInfo`

```
NotificationTypeInfo(
    TypeName: string,           // global:: FQN of the notification type
    ExecutionStrategy: int,     // enum cast to int
    ErrorStrategy: int          // enum cast to int
)
```

---

## Pipeline 3: Behaviors

### Predicate — `IsBehaviorCandidate`

Same as `IsHandlerCandidate`:

```csharp
return node is ClassDeclarationSyntax classDecl
       && classDecl.BaseList is not null
       && !classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);
```

### Transform — `GetBehaviorInfo`

1. Gets `INamedTypeSymbol`, checks abstract/null.
2. Checks for `[MediatorGeneration(Skip = true)]` — returns `null` if present.
3. Determines `IsOpenGeneric`: `classSymbol.IsGenericType && !classSymbol.IsUnboundGenericType && classSymbol.TypeParameters.Length > 0`.
4. Iterates `classSymbol.AllInterfaces` for `MediatorLite.IPipelineBehavior<TRequest, TResponse>`:
   - Checks if the interface type arguments are type parameters (`TypeKind.TypeParameter`) → `isInterfaceOpen`.
   - Captures `RequestType` and `ResponseType` (null when open), and `IsOpenGeneric` flag.
5. Returns `null` if no matching interfaces.

### Output Records

**`BehaviorInfo`**:
```
BehaviorInfo(
    ClassName: string,          // global:: FQN of the behavior class
    Namespace: string,
    BehaviorInterfaces: List<BehaviorInterfaceInfo>,
    IsOpenGeneric: bool         // true for behaviors like LoggingBehavior<TReq, TRes>
)
```

**`BehaviorInterfaceInfo`**:
```
BehaviorInterfaceInfo(
    InterfaceType: string,      // global:: FQN of IPipelineBehavior<...>
    RequestType: string?,       // null when open generic
    ResponseType: string?,      // null when open generic
    IsOpenGeneric: bool
)
```

---

## Pipeline 4: Validators

### Predicate — `IsValidatorCandidate`

Same shape as handler/behavior candidates:

```csharp
return node is ClassDeclarationSyntax classDecl
       && classDecl.BaseList is not null
       && !classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);
```

### Transform — `GetValidatorInfo`

1. Gets `INamedTypeSymbol`, checks abstract/null.
2. **Skips open-generic validators** (`classSymbol.IsGenericType` → return `null`). This prevents `DataAnnotationsValidator<T>` from the core library from being discovered.
3. Checks for `[MediatorGeneration(Skip = true)]`.
4. Scans `AllInterfaces` for `MediatorLite.Validation.IValidator<TRequest>`.
5. Only captures validators where the request type argument is concrete (not `TypeKind.TypeParameter`).

### Output Record — `ValidatorInfo`

```
ValidatorInfo(
    ClassName: string,          // global:: FQN of the validator class
    Namespace: string,
    InterfaceType: string,      // e.g. global::MediatorLite.Validation.IValidator<global::MyApp.CreateCommand>
    RequestType: string         // e.g. global::MyApp.CreateCommand
)
```

---

## `ExpandBehaviors` Method

Closes open-generic behaviors over every discovered request/response pair.

### Algorithm

1. Collects all distinct `(RequestType, ResponseType)` pairs from discovered request handlers.
2. For each `BehaviorInfo`:
   - If `IsOpenGeneric` and the interface is open:
     - Strips the generic arguments from `ClassName` (everything from `<` onward).
     - Creates a closed type: `{baseTypeName}<{requestType}, {responseType}>`.
     - Creates a closed interface: `global::MediatorLite.IPipelineBehavior<{requestType}, {responseType}>`.
     - Adds one `ExpandedBehaviorInfo` per request/response pair.
   - If the interface is **not** open (concrete behavior):
     - Adds a single `ExpandedBehaviorInfo` directly.

### Output Record — `ExpandedBehaviorInfo`

```
ExpandedBehaviorInfo(
    BehaviorTypeName: string,   // e.g. global::MyApp.LoggingBehavior<global::MyApp.CreateCommand, global::MediatorLite.Unit>
    RequestType: string,
    ResponseType: string,
    InterfaceType: string       // e.g. global::MediatorLite.IPipelineBehavior<...>
)
```

---

## `DetermineValidationTargets` Method

Identifies request types that need a `ValidationBehavior<TReq, TRes>` registration.

### Algorithm

1. Builds a `HashSet<string>` of request types that have custom validators (from `ValidatorInfo.RequestType`).
2. Collects all distinct `(RequestType, ResponseType, HasDataAnnotations)` tuples from handler interfaces.
3. A request type needs validation if:
   - `HasDataAnnotations` is true (request properties carry `[Required]`, `[MaxLength]`, etc.), **OR**
   - A concrete custom validator exists for that request type.
4. Returns a list of `(RequestType, ResponseType)` tuples.

These tuples drive:
- `AddGeneratedValidators()` — registers `DataAnnotationsValidator<T>` for types with DataAnnotation attributes.
- `AddGeneratedBehaviors()` — registers `ValidationBehavior<TReq, TRes>` **first** in the behavior pipeline.
- `InvokeBehavior` dispatch — adds `ExpandedBehaviorInfo` entries for `ValidationBehavior<TReq, TRes>` so they can be invoked without reflection.
