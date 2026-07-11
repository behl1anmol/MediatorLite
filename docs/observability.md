# Observability

MediatorLite provides built-in observability through logging and OpenTelemetry tracing.

## v2 Note

In v2, observability has moved to **compile time**. The source generator emits `ILogger` calls and `ActivitySource` events inline into every generated `Send_*` and `Publish_*` method. Both are on by default; you opt out at compile time with assembly-level attributes.

## Logging Configuration

### Enable / Disable Built-in Logging

Logging is on by default. To disable it entirely, add this to any `.cs` file in the consuming assembly:

```csharp
[assembly: DisableMediatorLogging]
```

When the attribute is present the generator simply omits the logging calls — there is no runtime branch or dead code.

### Controlling the Log Level

Generated code always calls `LogDebug` under the `MediatorLite.IMediator` category. Control verbosity through standard `Microsoft.Extensions.Logging` filters:

```csharp
services.AddLogging(builder =>
{
    builder.AddFilter("MediatorLite.IMediator", LogLevel.Information);
});
```

Or via `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "MediatorLite.IMediator": "Information"
    }
  }
}
```

## OpenTelemetry Integration

### Enable / Disable Tracing

Tracing is on by default. To disable it entirely, add to any `.cs` file in the consuming assembly:

```csharp
[assembly: DisableMediatorTracing]
```

### Configure OpenTelemetry

```csharp
services.AddOpenTelemetry()
    .WithTracing(builder =>
    {
        builder.AddSource("MediatorLite");  // Add MediatorLite source
        builder.AddConsoleExporter();
    });
```

### Activity Tags

MediatorLite activities include these tags:

| Tag | Description |
|-----|-------------|
| `mediatorlite.request.type` | Full type name of request |
| `mediatorlite.response.type` | Full type name of response |
| `mediatorlite.notification.type` | Full type name of notification |
| `mediatorlite.handler.type` | Handler type name |
| `mediatorlite.handler.count` | Number of notification handlers |
| `mediatorlite.execution.strategy` | Notification execution strategy |
| `error` | Boolean indicating error occurred |
| `error.message` | Error message if applicable |

## Diagnostic Events (reserved — not currently emitted)

`MediatorDiagnostics.Listener` (a `DiagnosticListener` named `"MediatorLite"`) and the
event-name constants under `MediatorDiagnostics.Events` (`MediatorLite.RequestStarted`,
`MediatorLite.RequestCompleted`, `MediatorLite.RequestFailed`,
`MediatorLite.NotificationPublished`, `MediatorLite.NotificationHandlerStarted`,
`MediatorLite.NotificationHandlerCompleted`) are a **reserved surface**: no MediatorLite
code writes to the listener today, so subscribing to it observes nothing.

Use the two live instrumentation channels instead:

- **Logging** — the `MediatorLite.IMediator` logger category (see above).
- **Tracing** — the `"MediatorLite"` `ActivitySource` (see above).

## Custom Logging Behavior

```csharp
public class DetailedLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<DetailedLoggingBehavior<TRequest, TResponse>> _logger;

    public DetailedLoggingBehavior(ILogger<DetailedLoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestType"] = typeof(TRequest).Name,
            ["CorrelationId"] = Guid.NewGuid()
        });

        _logger.LogInformation("Handling {RequestType}: {@Request}", 
            typeof(TRequest).Name, request);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();
            stopwatch.Stop();

            _logger.LogInformation(
                "Handled {RequestType} in {ElapsedMs}ms: {@Response}",
                typeof(TRequest).Name, stopwatch.ElapsedMilliseconds, response);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling {RequestType}", typeof(TRequest).Name);
            throw;
        }
    }
}
```
