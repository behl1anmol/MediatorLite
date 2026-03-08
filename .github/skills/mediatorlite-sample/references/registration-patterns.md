# Registration Patterns — MediatorLite.Sample

## Full Manual DI Registration (from Program.cs)

The sample uses top-level statements. The complete registration sequence:

```csharp
using MediatorLite;
using MediatorLite.Sample.Requests;
using MediatorLite.Sample.Handlers;
using MediatorLite.Sample.Notifications;
using MediatorLite.Sample.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();

// 1. Add logging infrastructure
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// 2. Register request handlers manually
services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserQueryHandler>();
services.AddTransient<IRequestHandler<DeleteUserCommand, Unit>, DeleteUserCommandHandler>();
services.AddTransient<IRequestHandler<CreateOrderCommand, int>, CreateOrderCommandHandler>();

// 3. Register notification handlers
services.AddTransient<INotificationHandler<UserCreatedNotification>, SendWelcomeEmailHandler>();
services.AddTransient<INotificationHandler<UserCreatedNotification>, CreateAuditLogHandler>();

// 4. Register open generic behaviors
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// 5. Add MediatorLite with options
services.AddMediatorLite(options =>
{
    options.EnableBuiltInLogging = true;
    options.EnableTracing = true;
});

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();
```

## How to Register Request Handlers

Each handler must be registered against the closed generic `IRequestHandler<TRequest, TResponse>` interface:

```csharp
// Query handler (returns a response type)
services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserQueryHandler>();

// Command handler returning a value
services.AddTransient<IRequestHandler<CreateOrderCommand, int>, CreateOrderCommandHandler>();

// Void command handler — register with Unit as the response type
// Even though the handler class implements IRequestHandler<DeleteUserCommand> (single type param),
// the DI registration must use the full two-type-param interface with Unit.
services.AddTransient<IRequestHandler<DeleteUserCommand, Unit>, DeleteUserCommandHandler>();
```

The handler lifetime is `Transient` by convention. You can use `AddScoped` or `AddSingleton` depending on your needs, but `Transient` is the default and matches what `MediatorOptions.HandlerLifetime` uses when source-gen is in play.

## How to Register Notification Handlers

Each notification handler is registered against `INotificationHandler<TNotification>`. Multiple handlers for the same notification type are registered separately:

```csharp
services.AddTransient<INotificationHandler<UserCreatedNotification>, SendWelcomeEmailHandler>();
services.AddTransient<INotificationHandler<UserCreatedNotification>, CreateAuditLogHandler>();
```

Execution order is controlled by `[NotificationHandlerOrder(n)]` on the handler class, not by registration order. Lower values run first.

## How to Register Open Generic Behaviors

Open generic behaviors apply to all requests. Register using the non-generic `AddTransient` overload with `typeof`:

```csharp
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

This is distinct from the `MediatorOptions.AddOpenBehavior(typeof(LoggingBehavior<,>))` approach. Both work. The difference:

| Approach | Where to register | When to use |
|----------|-------------------|-------------|
| `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(…))` | Before `AddMediatorLite()` | Manual DI — you control the lifetime and registration directly |
| `options.AddOpenBehavior(typeof(…))` | Inside `AddMediatorLite(options => { … })` | Source-gen or when you want `AddMediatorLite` to handle the DI registration for you |

The sample uses the direct `services.AddTransient` approach because it is fully manual DI.

## MediatorOptions Configuration

The sample configures two options:

```csharp
services.AddMediatorLite(options =>
{
    options.EnableBuiltInLogging = true;   // Enable built-in ILogger-based request logging
    options.EnableTracing = true;          // Enable OpenTelemetry ActivitySource tracing
});
```

Other available options (not set in the sample, using defaults):

| Option | Default | Description |
|--------|---------|-------------|
| `NotificationExecutionStrategy` | `Sequential` | How notification handlers run (Sequential, Parallel, StopOnFirst) |
| `NotificationErrorStrategy` | `ContinueAndAggregate` | How handler errors are collected |
| `DefaultLogLevel` | `LogLevel.Debug` | Log level for built-in logging |
| `HandlerLifetime` | `ServiceLifetime.Transient` | DI lifetime for auto-registered handlers (source-gen path) |
| `MediatorLifetime` | `ServiceLifetime.Transient` | DI lifetime for the `IMediator` registration |

## Reflection Fallback Dispatch

Because the sample does **not** register `ISourceGeneratedMediator` (no call to `AddGeneratedHandlers()`), the `Mediator` class uses the reflection-based dispatch path:

1. `Mediator` constructor resolves `ISourceGeneratedMediator?` from DI → gets `null`.
2. On `SendAsync`, since source-gen returns `null`, the mediator builds a closed generic type (`IRequestHandler<TRequest, TResponse>`) via `MakeGenericType`.
3. The constructed type is cached in a `ConcurrentDictionary` to avoid repeated reflection on subsequent calls.
4. The handler is resolved from `IServiceProvider` and invoked via reflection. `ExceptionDispatchInfo.Capture` preserves the original stack trace.
5. If no handler is registered → `InvalidOperationException`.

This is transparent to the calling code — `mediator.SendAsync(…)` works identically whether using source-gen or reflection dispatch.

## Adding a New Handler to the Sample

To add a new request handler:

1. Create the request record in `Requests/`:
   ```csharp
   public record UpdateUserCommand(int Id, string Name) : IRequest;
   ```

2. Create the handler in `Handlers/`:
   ```csharp
   public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand>
   {
       private readonly ILogger<UpdateUserCommandHandler> _logger;

       public UpdateUserCommandHandler(ILogger<UpdateUserCommandHandler> logger)
       {
           _logger = logger;
       }

       public ValueTask HandleAsync(UpdateUserCommand request, CancellationToken cancellationToken = default)
       {
           _logger.LogInformation("Updating user {Id} to name {Name}", request.Id, request.Name);
           return ValueTask.CompletedTask;
       }
   }
   ```

3. Register in `Program.cs`:
   ```csharp
   services.AddTransient<IRequestHandler<UpdateUserCommand, Unit>, UpdateUserCommandHandler>();
   ```

4. Add a usage example at the bottom of `Program.cs`:
   ```csharp
   await mediator.SendAsync(new UpdateUserCommand(42, "Jane Doe"));
   ```
