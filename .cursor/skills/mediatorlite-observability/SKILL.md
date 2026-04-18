---
name: mediatorlite-observability
description: Correctly wire, filter, and reason about MediatorLite's compile-time logging and OpenTelemetry tracing. Covers the `MediatorLite.IMediator` logger category (fixed at `LogDebug`), the `"MediatorLite"` `ActivitySource`, standard activity tags, the no-arg assembly opt-out attributes `[assembly: DisableMediatorLogging]` and `[assembly: DisableMediatorTracing]`, and the OpenTelemetry registration snippet. Use when adding, diagnosing, or removing observability emissions, when a consumer asks why logs/traces are (not) appearing, or when tuning log verbosity and OTEL exporters.
triggers: observability, logging, tracing, OpenTelemetry, OTEL, ActivitySource, MediatorLite.IMediator, LogDebug, DisableMediatorLogging, DisableMediatorTracing, MediatorActivitySource, mediatorlite.request.type, DiagnosticListener, AddFilter MediatorLite, OTLP exporter MediatorLite
---

# MediatorLite Observability

## Purpose

MediatorLite emits **two** orthogonal observability signals at dispatch boundaries:

1. **Structured logs** under the fixed logger category `MediatorLite.IMediator` at `LogDebug`.
2. **Distributed traces** via `System.Diagnostics.ActivitySource` named `"MediatorLite"`, with a small set of well-known tags.

Both are **emitted by the source generator directly into each generated `Pipeline_*` / `Publish_*` method**. There are no pipeline behaviors, no runtime branches, and no allocations when the consumer has opted out — the generator simply does not emit the corresponding statements. This skill teaches you how to reason about, configure, filter, and opt out of these two surfaces.

## When to use

- A consumer reports "I see no logs / no spans for MediatorLite" — use this skill to verify category, level, ActivitySource name, and opt-out attributes.
- You're wiring OpenTelemetry in a new host and need the correct `AddSource` argument.
- You're writing or reviewing benchmarks and need to neutralise logging/tracing overhead.
- You're evolving the generator's emission code (`HandlerDiscoveryGenerator`) or the diagnostic constants in `MediatorDiagnostics`.
- You're auditing generated code after a build to confirm logging/tracing really was stripped.

## Design invariants

- **Category is fixed.** Generated logs always use `ILogger<MediatorLite.IMediator>` (i.e. the category string is literally `MediatorLite.IMediator`). There is no per-handler category and no configuration that changes it.
- **Level is fixed at `LogDebug`.** There is no attribute or option that changes the level. If you want `Information`-level boundaries, write your own behavior (see *Custom logging behavior* below).
- **ActivitySource name is fixed at `"MediatorLite"`.** You must `AddSource("MediatorLite")` in your OpenTelemetry tracer for anything to be captured.
- **Opt-out is compile-time only.** `[assembly: DisableMediatorLogging]` / `[assembly: DisableMediatorTracing]` are `AttributeTargets.Assembly`, no-arg, `AllowMultiple = false`. They have no runtime effect — the generator simply omits the emissions.
- **Tracing is branch-free on the hot path when no listener is attached.** `ActivitySource.StartActivity` returns `null` when no listener is subscribed, so the `__activity?.SetTag(...)` calls are no-ops. You do not need to disable tracing for raw-throughput workloads unless you also want zero code bytes.
- **The mediator contract is fully documented** for observability on `AddMediatorLite`:

```17:29:src/MediatorLite/Configuration/ServiceCollectionExtensions.cs
    /// <remarks>
    /// <para>
    /// Built-in logging and tracing are on by default. To opt out at compile time, apply
    /// <c>[assembly: DisableMediatorLogging]</c> or <c>[assembly: DisableMediatorTracing]</c>
    /// in the consuming assembly.
    /// </para>
    /// <para>
    /// Log level is controlled via <c>Microsoft.Extensions.Logging</c> filter configuration.
    /// Notification execution and error strategies are compile-time only — use
    /// <c>[NotificationExecution]</c>/<c>[NotificationError]</c> per type or
    /// <c>[assembly: DefaultNotificationExecution]</c>/<c>[assembly: DefaultNotificationError]</c>.
    /// </para>
    /// </remarks>
```

## Entry points

- Attribute definitions: [Attributes.cs](src/MediatorLite.Abstractions/Abstractions/Attributes.cs) lines 227-268.
- Diagnostic constants and `DiagnosticListener`: [MediatorDiagnostics.cs](src/MediatorLite/Diagnostics/MediatorDiagnostics.cs).
- Generator emission sites: [HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs) around the `__logger` / `__activity` blocks.
- End-user documentation: [docs/observability.md](docs/observability.md).
- Reference opt-out example used by the benchmark projects: [AssemblyInfo.cs](tests/MediatorLite.Benchmarks/AssemblyInfo.cs).

## Logging API

### Category & level

Every generated log statement is issued through `ILogger<MediatorLite.IMediator>` at `Debug` level. To control verbosity, configure the standard `Microsoft.Extensions.Logging` filter pipeline:

```csharp
services.AddLogging(b =>
{
    b.AddFilter("MediatorLite.IMediator", LogLevel.Information);
});
```

Or via `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "MediatorLite.IMediator": "Debug"
    }
  }
}
```

Because the generator emits `LogDebug`, the filter **must allow `Debug`** for the `MediatorLite.IMediator` category to see anything. Most production `ILoggerFactory` setups default to `Information` — this is the #1 reason "I see no logs".

### Opt out at compile time

```246:247:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DisableMediatorLoggingAttribute : Attribute { }
```

Apply it in the **consuming assembly** (the one that calls `AddGeneratedHandlers()`):

```1:4:tests/MediatorLite.Benchmarks/AssemblyInfo.cs
using MediatorLite;

[assembly: DisableMediatorLogging]
[assembly: DisableMediatorTracing]
```

After applying, rebuild and grep the generated `*.g.cs` output for `__logger` — it should be gone for every `Pipeline_*` / `Publish_*` method.

### Custom logging behavior

If you need `Information`-level boundaries, structured properties, or correlation IDs, do not modify the generator — write an ordinary `IPipelineBehavior<TRequest, TResponse>` and use `[BehaviorOrder]` to position it. The example in [docs/observability.md](docs/observability.md) around `DetailedLoggingBehavior` is the canonical pattern. The built-in `LogDebug` emission is intentionally minimal and intended to be complementary to custom behaviors.

## Tracing API

### ActivitySource

```8:23:src/MediatorLite/Diagnostics/MediatorDiagnostics.cs
public static class MediatorActivitySource
{
    /// <summary>
    /// The name of the activity source.
    /// </summary>
    public const string SourceName = "MediatorLite";

    /// <summary>
    /// The version of the activity source.
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// The ActivitySource for MediatorLite tracing.
    /// </summary>
    public static readonly ActivitySource Source = new(SourceName, Version);
```

Activity names are constants exposed via `MediatorActivitySource.ActivityNames`:

```28:41:src/MediatorLite/Diagnostics/MediatorDiagnostics.cs
    public static class ActivityNames
    {
        /// <summary>Send request activity name prefix.</summary>
        public const string SendRequest = "MediatorLite.Send";

        /// <summary>Publish notification activity name prefix.</summary>
        public const string PublishNotification = "MediatorLite.Publish";

        /// <summary>Pipeline behavior activity name prefix.</summary>
        public const string PipelineBehavior = "MediatorLite.Behavior";

        /// <summary>Notification handler activity name prefix.</summary>
        public const string NotificationHandler = "MediatorLite.NotificationHandler";
    }
```

The generator emits activities shaped as `"MediatorLite.Send {SimpleRequestTypeName}"` and `"MediatorLite.Publish {SimpleNotificationTypeName}"`.

### Activity tags

```46:74:src/MediatorLite/Diagnostics/MediatorDiagnostics.cs
    public static class Tags
    {
        /// <summary>Request type tag.</summary>
        public const string RequestType = "mediatorlite.request.type";

        /// <summary>Response type tag.</summary>
        public const string ResponseType = "mediatorlite.response.type";

        /// <summary>Notification type tag.</summary>
        public const string NotificationType = "mediatorlite.notification.type";

        /// <summary>Handler type tag.</summary>
        public const string HandlerType = "mediatorlite.handler.type";

        /// <summary>Behavior type tag.</summary>
        public const string BehaviorType = "mediatorlite.behavior.type";

        /// <summary>Handler count tag.</summary>
        public const string HandlerCount = "mediatorlite.handler.count";

        /// <summary>Execution strategy tag.</summary>
        public const string ExecutionStrategy = "mediatorlite.execution.strategy";

        /// <summary>Error tag.</summary>
        public const string Error = "error";

        /// <summary>Error message tag.</summary>
        public const string ErrorMessage = "error.message";
    }
```

The generator always sets `RequestType` / `ResponseType` (or `NotificationType`). On failure it adds `error = true` and `error.message = ex.Message` before rethrowing.

### OpenTelemetry setup

```csharp
services.AddOpenTelemetry()
    .WithTracing(b =>
    {
        b.AddSource("MediatorLite");
        b.AddConsoleExporter();
        // b.AddOtlpExporter();
    });
```

**Always use the string literal `"MediatorLite"`**, or the `MediatorActivitySource.SourceName` constant. If the name does not match exactly, the `ActivityListener` will reject spans and you'll see nothing.

### Opt out at compile time

```267:268:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DisableMediatorTracingAttribute : Attribute { }
```

Same pattern as logging — applied in the consuming assembly. Benchmarks should always apply both to get a clean baseline.

## DiagnosticListener events

In addition to `ActivitySource`, MediatorLite exposes a legacy `DiagnosticListener` with the same `"MediatorLite"` name for consumers who prefer the `DiagnosticSource` pattern:

```83:109:src/MediatorLite/Diagnostics/MediatorDiagnostics.cs
    public static readonly DiagnosticListener Listener = new("MediatorLite");

    /// <summary>
    /// Event name constants.
    /// </summary>
    public static class Events
    {
        /// <summary>Request started event.</summary>
        public const string RequestStarted = "MediatorLite.RequestStarted";

        /// <summary>Request completed event.</summary>
        public const string RequestCompleted = "MediatorLite.RequestCompleted";

        /// <summary>Request failed event.</summary>
        public const string RequestFailed = "MediatorLite.RequestFailed";

        /// <summary>Notification published event.</summary>
        public const string NotificationPublished = "MediatorLite.NotificationPublished";

        /// <summary>Notification handler started event.</summary>
        public const string NotificationHandlerStarted = "MediatorLite.NotificationHandlerStarted";

        /// <summary>Notification handler completed event.</summary>
        public const string NotificationHandlerCompleted = "MediatorLite.NotificationHandlerCompleted";
    }
```

These event names remain stable; new events require an additive change.

## Common tasks

### 1. Wire MediatorLite into an existing OTEL pipeline

1. Add the NuGet references (`OpenTelemetry.Extensions.Hosting`, an exporter).
2. In `Program.cs`:
   ```csharp
   services.AddOpenTelemetry().WithTracing(b => b.AddSource("MediatorLite").AddOtlpExporter());
   ```
3. Confirm the consuming assembly does **not** have `[assembly: DisableMediatorTracing]`.
4. Send a request and verify a span named `MediatorLite.Send <RequestName>` with tag `mediatorlite.request.type`.

### 2. Bump the log level for MediatorLite without affecting other categories

Use `AddFilter("MediatorLite.IMediator", LogLevel.Debug)`. Do **not** change the global minimum — you will drown every other namespace.

### 3. Achieve true zero-overhead dispatch in benchmarks

Apply both opt-outs (mirror [tests/MediatorLite.Benchmarks/AssemblyInfo.cs](tests/MediatorLite.Benchmarks/AssemblyInfo.cs)). Verify by:

```powershell
dotnet build tests/MediatorLite.Benchmarks
# Then inspect obj/.../generated/*.g.cs — no __logger or __activity should appear.
```

### 4. Prove a span really fires

Attach a minimal `ActivityListener` in a test:

```csharp
using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name == "MediatorLite",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = a => captured.Add(a),
};
ActivitySource.AddActivityListener(listener);
```

## Pitfalls

- **Filter too low.** The default logger minimum is `Information`; without `AddFilter("MediatorLite.IMediator", LogLevel.Debug)` (or lower), you will see zero MediatorLite logs.
- **Wrong ActivitySource name.** `AddSource("MediatorLite.IMediator")` or `AddSource("MediatorLite.Tracing")` silently yields no spans. Always use `"MediatorLite"` (or `MediatorActivitySource.SourceName`).
- **Opt-out applied in the wrong assembly.** The generator runs in the assembly where `AddGeneratedHandlers()` is called. Putting `[assembly: DisableMediatorLogging]` in an upstream shared library has no effect on the downstream host's generated code.
- **Expecting attribute parameters.** Both opt-out attributes are no-arg. An older draft exposed `Enabled` / `LogLevel` / `IncludePayload` — that surface was removed. Do not add it back without a design discussion.
- **Enabling tracing at runtime by "adding the behavior".** There is no `TracingBehavior` — tracing is inlined. Writing a custom `ILoggingBehavior` is fine; writing a custom `ITracingBehavior` will just duplicate activity creation.
- **Removing `MediatorActivitySource` constants.** They are consumed both by the runtime (if you ever add listeners) and by the generated code via `global::MediatorLite.Diagnostics.MediatorActivitySource.Tags.*`. Renaming is a breaking change.
- **Notification handler spans.** The generator currently creates a single `MediatorLite.Publish <Notification>` activity per publish, not one per handler. Per-handler spans require a custom wrapper.

## Related

- [docs/observability.md](docs/observability.md) — end-user reference.
- [.cursor/skills/mediatorlite-abstractions/SKILL.md](.cursor/skills/mediatorlite-abstractions/SKILL.md) — for the attribute surface (DisableMediatorLogging / DisableMediatorTracing).
- [.github/copilot-instructions.md](.github/copilot-instructions.md) — short architecture map.
- [docs/pipeline-behaviors.md](docs/pipeline-behaviors.md) — how to author custom behaviors that complement built-in emissions.
- [tests/MediatorLite.Benchmarks/AssemblyInfo.cs](tests/MediatorLite.Benchmarks/AssemblyInfo.cs) — canonical opt-out pattern.
