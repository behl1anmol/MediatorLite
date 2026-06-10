# Source Generator Rules

The generator lives in `src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs`
and is the only producer of `MediatorLite.Generated.MediatorLiteRegistration`.

## Rule 1 — Must be `IIncrementalGenerator`

Never use the legacy `ISourceGenerator`. The current wiring is incremental and
uses `SyntaxProvider` + `CompilationProvider`:

```28:75:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all class declarations that might be handlers
        var handlerDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsHandlerCandidate(node),
                transform: static (context, ct) => GetHandlerInfo(context, ct))
            .Where(static info => info is not null);
```

- Predicates must be `static` and cheap; do not capture `this`.
- Don't walk `Compilation.GlobalNamespace` or do whole-assembly symbol
  enumeration — it destroys incrementality.

## Rule 2 — No runtime reflection of the generator's own assembly

The generator targets `netstandard2.0` and cannot reference the runtime
MediatorLite assembly. Keep semantic comparisons based on
`ITypeSymbol.ToDisplayString(...)` / full names, not `typeof(...)` equality.

Mirrored-constant comments must stay in sync with
`src/MediatorLite/Diagnostics/MediatorDiagnostics.cs`:

```21:27:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
    // Mirrors constants in src/MediatorLite/Diagnostics/MediatorDiagnostics.cs.
    // Kept in sync manually because the generator project (netstandard2.0) cannot
    // reference the runtime MediatorLite assembly. If the runtime constants change,
    // update these literals to match.
    private const string ActivityNameSendRequest = "MediatorLite.Send";
    private const string ActivityNamePublishNotification = "MediatorLite.Publish";
```

## Rule 3 — Diagnostic counts are part of the public surface

Every generated `MediatorLiteRegistration` must expose these four `int` count
properties. Tests, samples, and benchmarks read them for sanity checks.

```834:843:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
        sb.AppendLine($"        public static int RequestHandlerCount => {requestHandlers.Count};");
        ...
        sb.AppendLine($"        public static int NotificationHandlerCount => {notificationHandlers.Count};");
        ...
        sb.AppendLine($"        public static int BehaviorCount => {nonValidationBehaviorCount + requestTypesWithValidation.Count};");
        ...
        sb.AppendLine($"        public static int ValidatorCount => {totalValidatorCount};");
```

The empty-assembly fallback must still emit all four with value `0`:

```638:641:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
                             public static int RequestHandlerCount => 0;
                             public static int NotificationHandlerCount => 0;
                             public static int BehaviorCount => 0;
                             public static int ValidatorCount => 0;
```

Renaming any of these is a breaking change.

## Rule 4 — Logging is inlined under category `MediatorLite.IMediator`

Generated `Send_*` / `Publish_*` methods call `LogDebug` through an
`ILogger<MediatorLite.IMediator>` resolved from the mediator's scoped service
provider (`_sp`). Emit the calls exactly as the existing template does so
consumers can filter on the category:

```1132:1133:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
            sb.AppendLine("            var __logger = _sp.GetRequiredService<global::Microsoft.Extensions.Logging.ILogger<global::MediatorLite.IMediator>>();");
            sb.AppendLine($"            __logger.LogDebug(\"Sending request {{RequestType}}\", \"{simpleRequest}\");");
```

- Always `LogDebug`, never `LogInformation`/`LogError`. Level is a consumer
  concern via `AddFilter("MediatorLite.IMediator", LogLevel.X)`.
- Skip the entire block when `[assembly: DisableMediatorLogging]` is present.

## Rule 5 — Tracing uses `ActivitySource "MediatorLite"`

Use the mirrored activity names (`MediatorLite.Send`,
`MediatorLite.Publish`). Skip tracing emission entirely when
`[assembly: DisableMediatorTracing]` is present. Opt-outs are no-arg
attributes; they are a presence check, not a flag read.
