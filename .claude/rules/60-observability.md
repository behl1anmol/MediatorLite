# Observability Rules

MediatorLite has two observability hooks and both are **on by default**,
inlined by the source generator into every `Send_*` / `Publish_*` method.
The opt-outs are compile-time no-arg assembly attributes.

## Rule 1 — Logger category is `MediatorLite.IMediator`

Generated code resolves `ILogger<MediatorLite.IMediator>` and emits a single
`LogDebug` line at the start and at successful completion of every request
and notification. Consumers configure verbosity via standard
`Microsoft.Extensions.Logging` filters, e.g.:

```csharp
builder.Logging.AddFilter("MediatorLite.IMediator", LogLevel.Information);
```

- Start/success lines are always `LogDebug`. Do not introduce `LogInformation`
  / `LogWarning` calls in generated code — the category-level filter is the
  only supported knob. The single exception is the emitted `catch` block,
  which logs the exception with `LogError(__ex, ...)` before rethrowing; that
  is shipped behavior consumers alert on, so keep it.
- Do not rename the category. The deleted `MediatorLoggingAttribute` (with
  its per-class `Enabled` / `IncludePayload` / `LogLevel`) was removed; do
  not reintroduce a per-type knob.

## Rule 2 — Tracing uses `ActivitySource "MediatorLite"`

All tracing instrumentation goes through the single `ActivitySource` in
`MediatorDiagnostics.cs`:

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

Tag names are fixed constants (use them, don't invent strings):

```47:67:src/MediatorLite/Diagnostics/MediatorDiagnostics.cs
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
```

OpenTelemetry consumers must register the source name `"MediatorLite"`.

## Rule 3 — Compile-time opt-outs are no-arg assembly attributes

```246:268:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DisableMediatorLoggingAttribute : Attribute { }

/// ...
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DisableMediatorTracingAttribute : Attribute { }
```

Usage:

```csharp
[assembly: MediatorLite.DisableMediatorLogging]
[assembly: MediatorLite.DisableMediatorTracing]
```

Rules:

- Both attributes are **no-arg**. Do not add a `bool Enabled` property or a
  per-category filter. Level filtering is the logger's job; source filtering
  is an `ActivityListener`'s job.
- The generator uses a presence check (attribute found ⇒ omit emission).
  There is no runtime fallback — opt-out is literally zero IL.
- Never import these attributes into `src/`. They are for consumer assemblies
  only (samples, tests, downstream apps).

## Rule 4 — Do not add a parallel instrumentation stack

Prometheus, metrics, or custom instrumentation must go through the existing
`MediatorActivitySource` surface. Adding a second `ActivitySource` inside
MediatorLite is not allowed.

Note: `MediatorDiagnostics.Listener` and the `MediatorDiagnostics.Events`
names are a **reserved, currently-silent surface** — no runtime or generated
code writes to that `DiagnosticListener` today, so do not point consumers at
it and do not start emitting to it ad hoc. Wiring it up for real is a design
decision (it adds work to every dispatch); until then the only live
instrumentation is the `MediatorLite.IMediator` log category and the
`"MediatorLite"` `ActivitySource`.
