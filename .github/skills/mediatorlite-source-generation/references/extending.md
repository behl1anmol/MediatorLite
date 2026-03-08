# Extending the Source Generator

How to add new discoverable types, integration details, and packaging requirements.

---

## Adding a New Discoverable Type

To add a new type that the source generator discovers and generates code for:

### 1. Add a `CreateSyntaxProvider` Pipeline

In `HandlerDiscoveryGenerator.Initialize()`, add a new pipeline:

```csharp
var myTypeDeclarations = context.SyntaxProvider
    .CreateSyntaxProvider(
        predicate: static (node, _) => IsMyTypeCandidate(node),
        transform: static (context, ct) => GetMyTypeInfo(context, ct))
    .Where(static info => info is not null);
```

### 2. Create a Data Record

Define a new `internal sealed record` to hold the discovered information:

```csharp
internal sealed record MyTypeInfo(
    string ClassName,
    string Namespace,
    string InterfaceType,
    // ... additional fields as needed
);
```

### 3. Implement Predicate and Transform

- **Predicate**: Syntactic filter — keep it fast, only check syntax node shape (e.g., `ClassDeclarationSyntax` with `BaseList`). No semantic model access.
- **Transform**: Semantic analysis — resolve the type symbol, check `AllInterfaces` for the target interface by string name, check for `[MediatorGeneration(Skip = true)]`, extract relevant data.

### 4. Combine into the Pipeline

Add the new declaration to the `Combine` chain:

```csharp
var compilationAndData = context.CompilationProvider
    .Combine(handlerDeclarations.Collect())
    .Combine(notificationDeclarations.Collect())
    .Combine(behaviorDeclarations.Collect())
    .Combine(validatorDeclarations.Collect())
    .Combine(myTypeDeclarations.Collect());  // Add here
```

Update the `RegisterSourceOutput` destructuring accordingly.

### 5. Add Generation Logic in `Execute`

Process the discovered data and emit registrations and/or dispatch code:

- Add lines to `GenerateRegistrationCode` for DI registrations in `MediatorLiteRegistration.g.cs`.
- Add methods/switch arms to `GenerateSourceGeneratedMediator` for dispatch in `SourceGeneratedMediator.g.cs`.
- Optionally add a new granular registration method (e.g., `AddGeneratedMyTypes()`).

---

## `GetSafeTypeName` Utility

Converts a fully-qualified type name into a valid C# identifier for use in generated method names:

```csharp
private static string GetSafeTypeName(string fullyQualifiedType)
{
    return fullyQualifiedType
        .Replace("global::", "")
        .Replace(".", "_")
        .Replace("<", "_")
        .Replace(">", "_")
        .Replace(",", "_")
        .Replace(" ", "");
}
```

### Examples

| Input | Output |
|---|---|
| `global::MyApp.Commands.CreateOrderCommand` | `MyApp_Commands_CreateOrderCommand` |
| `global::MyApp.Queries.GetUserQuery` | `MyApp_Queries_GetUserQuery` |
| `global::MyApp.Handler<global::MyApp.Req, global::MyApp.Res>` | `MyApp_Handler_MyApp_Req__MyApp_Res_` |

Used to generate per-request helper methods like `ResolveBehaviorsFor_{SafeName}`.

---

## Integration Contract

### `ISourceGeneratedMediator` Interface

Defined in the core library at `src/MediatorLite/Abstractions/ISourceGeneratedMediator.cs`. This is the shared contract between the generator and the core mediator.

**Key methods**:

| Method | Purpose |
|---|---|
| `TrySendAsync<TResponse>` | Dispatch request without behaviors |
| `TryInvokeHandlerAsync<TResponse>` | Inner handler call when behaviors present |
| `TryGetHandlerOrder(Type)` | Get `[NotificationHandlerOrder]` value |
| `TryGetNotificationOptions(Type)` | Get `[NotificationOptions]` strategies |
| `TryGetCachedHandlers<T>(IServiceProvider)` | Resolve typed notification handlers |
| `TryResolveBehaviors(IServiceProvider, Type, Type)` | Resolve behaviors without `MakeGenericType` |
| `InvokeHandler<TResponse>(Type, object, object, CancellationToken)` | Typed handler invocation |
| `InvokeBehavior<TResponse>(Type, Type, object, object, RequestHandlerDelegate<TResponse>, CancellationToken)` | Typed behavior invocation |

### No Compile-Time Dependency

The generator references MediatorLite types **only by their fully-qualified string names**:

- `"MediatorLite.IRequestHandler<TRequest, TResponse>"`
- `"MediatorLite.INotificationHandler<TNotification>"`
- `"MediatorLite.IPipelineBehavior<TRequest, TResponse>"`
- `"MediatorLite.Validation.IValidator<TRequest>"`
- `"MediatorLite.INotification"`

There is **no project reference** from `MediatorLite.SourceGeneration` to `MediatorLite`. This is intentional — source generators cannot reference the assemblies they analyze.

The generated code emits `global::MediatorLite.*` type references that resolve at compile time in the consumer project, which references both the core library and the generator.

---

## Consumer Project Setup

Consumer projects reference the source generator as an analyzer:

```xml
<ProjectReference Include="..\MediatorLite.SourceGeneration\MediatorLite.SourceGeneration.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Or via NuGet (the generator is automatically picked up from `analyzers/dotnet/cs`):

```xml
<PackageReference Include="MediatorLite.SourceGeneration" Version="x.y.z" />
```

The `DevelopmentDependency=true` flag ensures the generator package is not transitive — it won't appear in the consumer's published package dependencies.

---

## Packaging Details

From the `.csproj`:

```xml
<DevelopmentDependency>true</DevelopmentDependency>
<IncludeBuildOutput>false</IncludeBuildOutput>

<None Include="$(OutputPath)\$(AssemblyName).dll"
      Pack="true"
      PackagePath="analyzers/dotnet/cs"
      Visible="false" />
```

- **`DevelopmentDependency=true`**: The package is development-only. It is not listed as a dependency in consumer packages.
- **`IncludeBuildOutput=false`**: The main `lib/` folder is empty — consumers don't get a runtime assembly reference.
- **Analyzer DLL placement**: The compiled DLL is packed into `analyzers/dotnet/cs`, where NuGet automatically picks it up as a Roslyn analyzer/generator.
- **`NU5128` suppressed**: Warning about missing `lib/ref` assemblies is expected for analyzer-only packages.

### Additional Package Assets

- `README.md` — packed at root (`PackagePath="\"`).
- `icon.png` — packed at root for NuGet gallery display.

### Roslyn Dependencies

```xml
<PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
```

Both are `PrivateAssets="all"` — they are not exposed to consumers. The analyzer rules package enforces best practices for generator development.
