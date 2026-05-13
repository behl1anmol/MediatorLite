# Instruction: Extend the Source Generator

## Intent

Add a fifth discovery pipeline to [`HandlerDiscoveryGenerator`](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs) — alongside the existing request handler, notification handler, behavior, and validator pipelines — without breaking the `IIncrementalGenerator` contract, the public API of `MediatorLiteRegistration`, or the compile-time-only guarantee for notification strategies and logging/tracing.

## When to use

- Introducing a new kind of discovered type (e.g. `IAuthorizationPolicy<T>`, `IExceptionHandler<T>`, `IStreamHandler<T>`).
- Adding a new diagnostic count to `MediatorLiteRegistration`.
- Splitting an existing pipeline into two narrower ones when a discovery predicate grows unwieldy.

## Agent ownership

- **Primary:** `backend-developer` working on the generator.
- **Review gate:** `code-reviewer` — this touches `src/**` and a generator regression cannot be caught by runtime tests alone.
- **Tester:** writes the generator output / snapshot tests under [tests/MediatorLite.Tests/SourceGeneration/](tests/MediatorLite.Tests/SourceGeneration/).

## Inputs / Preconditions

- You have read [.claude/rules/20-source-generator.mdc](.claude/rules/20-source-generator.mdc) and understand the `IIncrementalGenerator` contract: the pipeline must be composed of equatable record/struct nodes, `CreateSyntaxProvider` must be cheap, and `RegisterSourceOutput` is the only place that writes source.
- The generator project targets `netstandard2.0` and **cannot** reference the runtime `MediatorLite` assembly. Constants like activity names are duplicated intentionally and kept in sync manually.

## Numbered steps

1. **Locate the four existing pipelines** in `Initialize`:

   ```28:56:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
       public void Initialize(IncrementalGeneratorInitializationContext context)
       {
           // Find all class declarations that might be handlers
           var handlerDeclarations = context.SyntaxProvider
               .CreateSyntaxProvider(
                   predicate: static (node, _) => IsHandlerCandidate(node),
                   transform: static (context, ct) => GetHandlerInfo(context, ct))
               .Where(static info => info is not null);

           // Find all notification types to capture their options
           var notificationDeclarations = context.SyntaxProvider
               .CreateSyntaxProvider(
                   predicate: static (node, _) => IsNotificationCandidate(node),
                   transform: static (context, ct) => GetNotificationInfo(context, ct))
               .Where(static info => info is not null);

           // Find all behavior declarations
           var behaviorDeclarations = context.SyntaxProvider
               .CreateSyntaxProvider(
                   predicate: static (node, _) => IsBehaviorCandidate(node),
                   transform: static (context, ct) => GetBehaviorInfo(context, ct))
               .Where(static info => info is not null);

           // Find all validator declarations
           var validatorDeclarations = context.SyntaxProvider
               .CreateSyntaxProvider(
                   predicate: static (node, _) => IsValidatorCandidate(node),
                   transform: static (context, ct) => GetValidatorInfo(context, ct))
               .Where(static info => info is not null);
   ```

2. **Add a fifth pipeline in the same shape**. Define an `IsMyNewThingCandidate` fast-syntax predicate and a `GetMyNewThingInfo` transform. The transform must return a fully materialised, equatable struct/record — do **not** return `INamedTypeSymbol` directly (it is not equatable).

   ```csharp
   var myNewThingDeclarations = context.SyntaxProvider
       .CreateSyntaxProvider(
           predicate: static (node, _) => IsMyNewThingCandidate(node),
           transform: static (context, ct) => GetMyNewThingInfo(context, ct))
       .Where(static info => info is not null);
   ```

3. **Combine into the source-output tuple**. Extend the `.Combine(...)` chain and destructure the new element:

   ```63:75:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
           // Combine with compilation
           var compilationAndData = context.CompilationProvider
               .Combine(handlerDeclarations.Collect())
               .Combine(notificationDeclarations.Collect())
               .Combine(behaviorDeclarations.Collect())
               .Combine(validatorDeclarations.Collect())
               .Combine(assemblyDefaults);

           // Generate the output
           context.RegisterSourceOutput(compilationAndData, static (spc, source) =>
           {
               var (((((compilation, handlers), notifications), behaviors), validators), defaults) = source;
               Execute(spc, compilation, handlers!, notifications!, behaviors!, validators!, defaults);
           });
   ```

   Extend `Execute` to accept the new collection and propagate it into the emitter.

4. **Emit a new `AddGenerated<MyNewThings>()` method** following the template used for request handlers/behaviors/validators in the emitter (see lines 664–786 of `HandlerDiscoveryGenerator.cs`). The new method must:
   - Live on `MediatorLite.Generated.MediatorLiteRegistration`.
   - Take `this IServiceCollection services` and return `IServiceCollection` for chaining.
   - Be additively included in `AddGeneratedHandlers()` so existing consumers don't need to change their call sites:

   ```647:650:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
           sb.AppendLine("            AddGeneratedRequestHandlers(services);");
           sb.AppendLine("            AddGeneratedNotificationHandlers(services);");
           sb.AppendLine("            AddGeneratedValidators(services);");
           sb.AppendLine("            AddGeneratedBehaviors(services);");
   ```

5. **Emit a new `MyNewThingCount` diagnostic constant**, mirroring the pattern:

   ```791:800:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
           sb.AppendLine($"        public static int RequestHandlerCount => {requestHandlers.Count};");
   ```

   And include a zero-default in the empty-compilation emitter alongside:

   ```597:600:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
                               public static int RequestHandlerCount => 0;
                               public static int NotificationHandlerCount => 0;
                               public static int BehaviorCount => 0;
                               public static int ValidatorCount => 0;
   ```

6. **Preserve incrementality.** Verify by scanning the pipeline: no method that flows through `CreateSyntaxProvider` / `Collect` should capture an `INamedTypeSymbol` directly. Only equatable data classes (records with value-based equality) may cross the cache boundary.

7. **Write a snapshot test** under [tests/MediatorLite.Tests/SourceGeneration/](tests/MediatorLite.Tests/SourceGeneration/). The test should:
   - Register two dummy `IMyNewThing` implementations.
   - Assert `MediatorLiteRegistration.MyNewThingCount == 2`.
   - Assert `AddGeneratedMyNewThings(services)` registers each type with `IServiceCollection` exactly once.

8. **Build and test end-to-end**:

   ```powershell
   dotnet build MediatorLite.sln -c Release
   dotnet test  MediatorLite.sln -c Release --no-build
   ```

   Both must return exit code `0` with no analyzer warnings (warnings-as-errors is on per [Directory.Build.props](Directory.Build.props)).

## Validation / Acceptance

- The generator remains an `IIncrementalGenerator` (`[Generator(LanguageNames.CSharp)] public sealed class HandlerDiscoveryGenerator : IIncrementalGenerator`) — no reversion to `ISourceGenerator`.
- `MediatorLiteRegistration` exposes `AddGeneratedMyNewThings()` and `MyNewThingCount` with the documented shape.
- `AddGeneratedHandlers()` invokes the new granular method so the recommended one-line wiring still registers everything.
- All existing tests still pass: `dotnet test MediatorLite.sln -c Release` exits `0`.
- `dotnet build` on `samples/MediatorLite.Sample.SourceGen` does **not** produce new warnings.

## Handoff / Exit criteria

- Hand back to the orchestrator with: the new pipeline name, emitted API additions (method + count), and the snapshot-test path.
- `code-reviewer` must sign off on incrementality (no symbol leaks across cache boundaries) and the `AddGeneratedHandlers()` composition.
- Record a `.github/Memories/` note describing the new discovered concept and how its predicate differs from the existing four.

## Related rules, skills, instructions

- Rules: [.claude/rules/20-source-generator.mdc](.claude/rules/20-source-generator.mdc), [.claude/rules/10-dispatch-invariants.mdc](.claude/rules/10-dispatch-invariants.mdc).
- Generator entry point: [src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs).
- Abstractions: [ISourceGeneratedMediator.cs](src/MediatorLite.Abstractions/Abstractions/ISourceGeneratedMediator.cs).
- Tests: [tests/MediatorLite.Tests/SourceGeneration/MediatorTests.cs](tests/MediatorLite.Tests/SourceGeneration/MediatorTests.cs).
- Agents: [.claude/agents/orchestrator.md](.claude/agents/orchestrator.md), [.github/agents/code-reviewer.agent.md](.github/agents/code-reviewer.agent.md).
- Related instructions: [add-new-request-handler.md](add-new-request-handler.md), [add-new-pipeline-behavior.md](add-new-pipeline-behavior.md).
