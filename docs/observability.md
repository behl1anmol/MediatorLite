# Observability

MediatorLite provides built-in observability through logging and OpenTelemetry tracing.

## Logging Configuration

### Enable/Disable Built-in Logging

```csharp
services.AddMediatorLite(options =>
{
    options.EnableBuiltInLogging = true;  // Default: true
    options.DefaultLogLevel = LogLevel.Debug;
});
```

### Per-Request Logging Control

```csharp
// Disable logging for high-frequency queries
[MediatorLogging(Enabled = false)]
public record HighFrequencyQuery : IRequest<Result>;

// Enable payload logging for audit
[MediatorLogging(IncludePayload = true, LogLevel = 2)] // LogLevel.Information
public record AuditableCommand : IRequest<Unit>;
```

## OpenTelemetry Integration

### Enable Tracing

```csharp
services.AddMediatorLite(options =>
{
    options.EnableTracing = true;  // Default: true
});
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

## Diagnostic Events

Subscribe to diagnostic events:

```csharp
DiagnosticListener.AllListeners.Subscribe(listener =>
{
    if (listener.Name == "MediatorLite")
    {
        listener.Subscribe(kvp =>
        {
            switch (kvp.Key)
            {
                case "MediatorLite.RequestStarted":
                    Console.WriteLine($"Request started: {kvp.Value}");
                    break;
                case "MediatorLite.RequestCompleted":
                    Console.WriteLine($"Request completed: {kvp.Value}");
                    break;
            }
        });
    }
});
```

### Available Events

| Event | Description |
|-------|-------------|
| `MediatorLite.RequestStarted` | Request handling began |
| `MediatorLite.RequestCompleted` | Request handling completed |
| `MediatorLite.RequestFailed` | Request handling failed |
| `MediatorLite.NotificationPublished` | Notification published |
| `MediatorLite.NotificationHandlerStarted` | Handler execution started |
| `MediatorLite.NotificationHandlerCompleted` | Handler execution completed |

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
