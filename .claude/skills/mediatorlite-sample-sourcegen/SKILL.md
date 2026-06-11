---
name: mediatorlite-sample-sourcegen
description: Reference for the MediatorLite.Sample.SourceGen console app -- the canonical consumer setup (services.AddGeneratedHandlers().AddMediatorLite()), project layout (Requests/, Handlers/, Behaviors/, Validators/, Notifications/), mixed open (PerformanceLoggingBehavior) + closed (PlaceOrderAuthorizationBehavior) pipeline behaviors, dual-layer validation (DataAnnotations + CreateProductCommandValidator), and ordered multi-handler notifications. Use when wiring up a new consumer project, demonstrating source-gen features, or learning reference layout.
triggers: MediatorLite sample, source-gen consumer, AddGeneratedHandlers usage, sample Program.cs, reference wiring, behavior composition example, PerformanceLoggingBehavior, PlaceOrderAuthorizationBehavior, CreateProductCommandValidator, OrderPlacedNotification, sample layout
---

# MediatorLite.Sample.SourceGen

> **⚠️ Validation note.** The sample's validation is now a single **FluentValidation**
> `AbstractValidator<CreateProductCommand>` (the old "dual-layer DataAnnotations + custom
> IValidator" model was removed). The sample references the `MediatorLite.FluentValidation`
> package. See [mediatorlite-validation](../mediatorlite-validation/SKILL.md).

## Purpose

`MediatorLite.Sample.SourceGen` is the reference consumer project that demonstrates the canonical end-to-end setup of MediatorLite: compile-time handler discovery, open + closed pipeline behaviors, automatic DataAnnotations validation merged with a custom `IValidator<T>`, and a multi-handler notification with `NotificationHandlerOrder`. The `Program.cs` prints `MediatorLiteRegistration.*Count` diagnostics at startup, then walks through six scenarios (query, search, place-order-with-notifications, valid create, DataAnnotations failure, business-rule failure). Use it as the copy-paste template for new projects.

## When to use

- Wiring MediatorLite into a new console / web / function app.
- Demonstrating behavior composition (open generic + request-specific closed behavior).
- Showing how DataAnnotations and a custom validator stack inside a single `ValidationBehavior`.
- Showing how to fan out notifications to multiple ordered handlers.
- Explaining the effect of `AddGeneratedHandlers()` / `AddMediatorLite()` on `IServiceCollection`.

## Project location & entry points

- [MediatorLite.Sample.SourceGen.csproj](samples/MediatorLite.Sample.SourceGen/MediatorLite.Sample.SourceGen.csproj)
- [Program.cs](samples/MediatorLite.Sample.SourceGen/Program.cs) — top-level statements driving six demos.
- [Requests/](samples/MediatorLite.Sample.SourceGen/Requests) — request record types (`GetProductQuery`, `SearchProductsQuery`, `PlaceOrderCommand`, `UpdateStockCommand`, `CreateProductCommand`).
- [Handlers/](samples/MediatorLite.Sample.SourceGen/Handlers) — one handler per request, plus the orchestrator `PlaceOrderCommandHandler`.
- [Behaviors/](samples/MediatorLite.Sample.SourceGen/Behaviors) — `PerformanceLoggingBehavior<,>` (open generic) and `PlaceOrderAuthorizationBehavior` (closed).
- [Validators/](samples/MediatorLite.Sample.SourceGen/Validators/CreateProductCommandValidator.cs) — custom `IValidator<CreateProductCommand>`.
- [Notifications/](samples/MediatorLite.Sample.SourceGen/Notifications) — `OrderPlacedNotification` + three handlers.

## Core types / API surface

### Canonical DI wiring (`Program.cs`, lines 34-57)

The sample shows the exact shape every consumer should use:

```34:57:samples/MediatorLite.Sample.SourceGen/Program.cs
var services = new ServiceCollection();

// Add logging
services.AddLogging(builder =>
{
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
});

// 🎯 Register source-generated handlers, behaviors, validators, and notifications
// The source generator discovers:
//   - All IRequestHandler implementations
//   - All INotificationHandler implementations
//   - All IPipelineBehavior implementations (open generic AND closed)
//   - All IValidator<T> implementations (custom validators)
//   - DataAnnotationsValidator<T> for request types with DataAnnotation attributes
//   - ValidationBehavior<,> registered FIRST to ensure validation short-circuits before other behaviors
// NO NEED to call options.AddOpenBehavior() or manually register behaviors/validators!
services.AddGeneratedHandlers();

// Add MediatorLite core services.
// Built-in logging + tracing are on by default; opt out via
// [assembly: DisableMediatorLogging] / [assembly: DisableMediatorTracing].
services.AddMediatorLite();
```

The comments are the contract: **don't** call `AddOpenBehavior` or `AddTransient<IValidator<T>, MyValidator>` yourself — the generator does it.

### Startup diagnostic dump

Before building the provider, the sample reads the generator's compile-time counts:

```21:31:samples/MediatorLite.Sample.SourceGen/Program.cs
Console.WriteLine($"📊 Source Generator Stats:");
Console.WriteLine($"   Request Handlers discovered: {MediatorLiteRegistration.RequestHandlerCount}");
Console.WriteLine($"   Notification Handlers discovered: {MediatorLiteRegistration.NotificationHandlerCount}");
Console.WriteLine($"   Pipeline Behaviors discovered: {MediatorLiteRegistration.BehaviorCount}");
Console.WriteLine($"      - PerformanceLoggingBehavior<,> (open generic - applies to all requests)");
Console.WriteLine($"      - PlaceOrderAuthorizationBehavior (closed - applies only to PlaceOrderCommand)");
Console.WriteLine($"      - ValidationBehavior<,> (auto-registered for validated request types)");
Console.WriteLine($"   Validators discovered: {MediatorLiteRegistration.ValidatorCount}");
Console.WriteLine($"      - DataAnnotationsValidator<CreateProductCommand> (auto-detected from attributes)");
Console.WriteLine($"      - CreateProductCommandValidator (custom business logic validator)");
Console.WriteLine();
```

### Open generic behavior

`PerformanceLoggingBehavior<TRequest, TResponse>` is a stopwatch-wrapping behavior registered for **every** request/response pair the generator discovers:

```10:52:samples/MediatorLite.Sample.SourceGen/Behaviors/PerformanceLoggingBehavior.cs
public sealed class PerformanceLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<PerformanceLoggingBehavior<TRequest, TResponse>> _logger;

    public PerformanceLoggingBehavior(ILogger<PerformanceLoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug("⏱️ Starting {RequestName}", requestName);

        try
        {
            var response = await next();
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > 500)
            {
                _logger.LogWarning("⚠️ {RequestName} took {ElapsedMs}ms (slow)", requestName, stopwatch.ElapsedMilliseconds);
            }
```

Note: No `[BehaviorOrder]` — it defaults to `0`.

### Closed, request-specific behavior

`PlaceOrderAuthorizationBehavior` implements `IPipelineBehavior<PlaceOrderCommand, OrderResult>` exactly — it is **not** generic, so it is applied only to that single request type:

```10:56:samples/MediatorLite.Sample.SourceGen/Behaviors/PlaceOrderAuthorizationBehavior.cs
public sealed class PlaceOrderAuthorizationBehavior
    : IPipelineBehavior<PlaceOrderCommand, OrderResult>
{
    private readonly ILogger<PlaceOrderAuthorizationBehavior> _logger;

    public PlaceOrderAuthorizationBehavior(ILogger<PlaceOrderAuthorizationBehavior> logger)
    {
        _logger = logger;
    }

    public async ValueTask<OrderResult> HandleAsync(
        PlaceOrderCommand request,
        RequestHandlerDelegate<OrderResult> next,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "🔒 [Closed Behavior] Authorizing order placement for product {ProductId}",
            request.ProductId);

        // Simulate authorization check
        if (request.Quantity > 100)
        {
            _logger.LogWarning(
                "⚠️ Large order detected ({Quantity} units) - requires manager approval",
                request.Quantity);
```

Short-circuit example: throwing inside the behavior stops the rest of the pipeline:

```43:50:samples/MediatorLite.Sample.SourceGen/Behaviors/PlaceOrderAuthorizationBehavior.cs
        // Check for suspicious patterns (demo only)
        if (string.IsNullOrWhiteSpace(request.CustomerEmail) ||
            !request.CustomerEmail.Contains('@'))
        {
            _logger.LogError("❌ Invalid customer email: {Email}", request.CustomerEmail);
            throw new InvalidOperationException(
                "Order authorization failed: invalid customer email");
        }
```

### DataAnnotations + custom validator layered together

`CreateProductCommand` uses DataAnnotations; the generator auto-registers `DataAnnotationsValidator<CreateProductCommand>`:

```9:28:samples/MediatorLite.Sample.SourceGen/Requests/CreateProductCommand.cs
public sealed record CreateProductCommand : IRequest<int>
{
    [Required(ErrorMessage = "Product name is required")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Product name must be between 3 and 100 characters")]
    public required string Name { get; init; }

    [Required(ErrorMessage = "Description is required")]
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public required string Description { get; init; }

    [Range(0.01, 10000.00, ErrorMessage = "Price must be between $0.01 and $10,000.00")]
    public decimal Price { get; init; }

    [Range(0, 10000, ErrorMessage = "Initial stock must be between 0 and 10,000")]
    public int InitialStock { get; init; }

    [Required(ErrorMessage = "Category is required")]
    [StringLength(50, ErrorMessage = "Category cannot exceed 50 characters")]
    public required string Category { get; init; }
}
```

`CreateProductCommandValidator` layers business rules on top. `ValidationBehavior` runs both validators and aggregates their errors into a single `ValidationException`:

```33:85:samples/MediatorLite.Sample.SourceGen/Validators/CreateProductCommandValidator.cs
    public ValueTask<ValidationResult> ValidateAsync(
        CreateProductCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Running custom business validation for product: {Name}", request.Name);

        var errors = new List<ValidationError>();

        // Business rule 1: Check for restricted product names
        if (RestrictedNames.Contains(request.Name))
        {
            errors.Add(new ValidationError(
                nameof(request.Name),
                $"Product name '{request.Name}' is restricted and cannot be used",
                request.Name));
        }

        // Business rule 2: Validate category against allowed list
        if (!AllowedCategories.Contains(request.Category))
        {
            errors.Add(new ValidationError(
                nameof(request.Category),
                $"Category '{request.Category}' is not valid. Allowed categories: {string.Join(", ", AllowedCategories)}",
                request.Category));
        }
```

`Program.cs` catches both failure modes in two separate demos (lines 134-194), printing the `ex.Errors` collection.

### Orchestrator handler — Send + Publish composition

`PlaceOrderCommandHandler` shows how a handler can re-enter the mediator for nested queries, commands, and notifications:

```20:43:samples/MediatorLite.Sample.SourceGen/Handlers/PlaceOrderCommandHandler.cs
    public async ValueTask<OrderResult> HandleAsync(PlaceOrderCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing order for ProductId: {ProductId}, Quantity: {Quantity}",
            request.ProductId, request.Quantity);

        // Get product to calculate price
        var product = await _mediator.SendAsync(new GetProductQuery(request.ProductId), cancellationToken);

        // Calculate total
        var totalAmount = product.Price * request.Quantity;
        var orderId = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";

        // Update stock
        await _mediator.SendAsync(new UpdateStockCommand(request.ProductId, -request.Quantity), cancellationToken);

        // Publish order placed notification
        await _mediator.PublishAsync(
            new Notifications.OrderPlacedNotification(orderId, request.ProductId, request.Quantity, request.CustomerEmail, totalAmount),
            cancellationToken);

        _logger.LogInformation("Order {OrderId} placed successfully. Total: {TotalAmount:C}", orderId, totalAmount);

        return new OrderResult(orderId, totalAmount, DateTime.UtcNow);
    }
```

### Ordered notification handlers

`OrderPlacedNotification` has three handlers — `OrderConfirmationEmailHandler`, `InventoryReservationHandler`, `OrderAuditLogHandler`. Each carries `[NotificationHandlerOrder(n)]`:

```1:11:samples/MediatorLite.Sample.SourceGen/Notifications/OrderPlacedNotification.cs
namespace MediatorLite.Sample.SourceGen.Notifications;

/// <summary>
/// Notification published when an order is placed.
/// </summary>
public sealed record OrderPlacedNotification(
    string OrderId,
    int ProductId,
    int Quantity,
    string CustomerEmail,
    decimal TotalAmount) : INotification;
```

```7:29:samples/MediatorLite.Sample.SourceGen/Notifications/OrderConfirmationEmailHandler.cs
/// <summary>
/// Handler that sends order confirmation email.
/// </summary>
[NotificationHandlerOrder(1)]
public sealed class OrderConfirmationEmailHandler : INotificationHandler<OrderPlacedNotification>
{
    private readonly ILogger<OrderConfirmationEmailHandler> _logger;

    public OrderConfirmationEmailHandler(ILogger<OrderConfirmationEmailHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask HandleAsync(OrderPlacedNotification notification, CancellationToken cancellationToken = default)
    {
        // Simulate sending email
        await Task.Delay(100, cancellationToken);

        _logger.LogInformation(
            "📧 Sent order confirmation email to {Email} for order {OrderId}. Total: {TotalAmount:C}",
            notification.CustomerEmail,
            notification.OrderId,
            notification.TotalAmount);
    }
}
```

Since the notification has no `[NotificationExecution]` attribute, the generator falls back to the library default (`Sequential` / `StopOnFirstError`) — handlers run in the declared order.

## Patterns & invariants

**Do:**
- Keep the `AddLogging` → `AddGeneratedHandlers` → `AddMediatorLite` order. Observability requires `ILogger<IMediator>` in the container.
- Separate types into folders: `Requests/`, `Handlers/`, `Behaviors/`, `Validators/`, `Notifications/`.
- Use `sealed record` for requests and notifications for value-equality and immutability.
- Use **open generic** behaviors for cross-cutting concerns that apply to every request (logging, tracing, metrics).
- Use **closed** behaviors for request-specific policies (authorization, domain-specific idempotency).
- Inject `IMediator` into handlers to orchestrate nested flows (query + command + notification).

**Don't:**
- Don't manually register handlers, behaviors, or validators. `AddGeneratedHandlers()` does it all.
- Don't throw non-`ValidationException` for validation failures in custom validators — return `ValidationResult.Failure(errors)` so `ValidationBehavior` aggregates across validators.
- Don't call `PublishAsync` inside a behavior unless you really need side-effecting notifications — the `PlaceOrderCommandHandler` handler is the right layer for that.

## Common tasks

1. **Add a new request + handler**
   1. Create a record in `Requests/` implementing `IRequest<TResponse>`.
   2. Create a handler in `Handlers/` implementing `IRequestHandler<TRequest, TResponse>`.
   3. Rebuild. `MediatorLiteRegistration.RequestHandlerCount` increments automatically.

2. **Add a new cross-cutting behavior**
   1. Create an open generic `IPipelineBehavior<TRequest, TResponse>` in `Behaviors/`.
   2. Optionally add `[BehaviorOrder(n)]` — lower runs first; validation always runs before.

3. **Add a new notification with multiple handlers**
   1. Create a record in `Notifications/` implementing `INotification`.
   2. Optionally add `[NotificationExecution(NotificationExecutionStrategy.Parallel)]` and `[NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]` on the record.
   3. Create handlers with `[NotificationHandlerOrder(n)]`.

4. **Add business-rule validation**
   1. Implement `IValidator<MyCommand>` (non-generic, closed) in `Validators/`.
   2. Add DataAnnotations to the command for primitive field rules; they merge automatically.

5. **Run the sample**
   - `dotnet run --project samples/MediatorLite.Sample.SourceGen`
   - Observe: generator stats at top, six demo scenarios with log output.

## Pitfalls & gotchas

- **Validation uses `MediatorLite.Validation.IValidator`**, not `FluentValidation.IValidator`. The sample uses `using MediatorLite.Validation;` plus `using MediatorLite.Validation.Models;` for `ValidationResult` / `ValidationError`.
- **`PlaceOrderAuthorizationBehavior` throws `InvalidOperationException`** rather than a validation exception — this is intentional because authorization is not the same as validation, and `ValidationBehavior` handles its own exception.
- **`PerformanceLoggingBehavior` does not have `[BehaviorOrder]`** — its default order (`0`) means it runs at the same level as other zero-ordered behaviors. In this sample, only validation runs before it; validation is emitted first by the generator regardless of behavior order.
- **Nested `SendAsync`/`PublishAsync` inside a handler** run each request's full pipeline (behaviors, logging, tracing). Fanning out many nested calls adds cumulative behavior overhead.
- **The sample targets `net10.0`** with nullable + implicit usings + warnings-as-errors via [Directory.Build.props](Directory.Build.props).
- **`[NotificationHandlerOrder]`** must be on the handler class, **not** on the notification.

## Related skills & rules

- **mediatorlite-abstractions** — interfaces and attributes used throughout the sample.
- **mediatorlite-core** — `AddMediatorLite()` runtime wiring the sample depends on.
- **mediatorlite-source-generation** — the generator reads the sample's handlers/behaviors/validators/notifications to produce registration + dispatch.
- **mediatorlite-tests** — equivalent patterns exercised in unit-level tests.
- Docs: [docs/quick-start.md](docs/quick-start.md), [docs/notifications.md](docs/notifications.md), [docs/validation.md](docs/validation.md), [docs/pipeline-behaviors.md](docs/pipeline-behaviors.md).
- [AGENTS.md](AGENTS.md): "Check `samples/MediatorLite.Sample.SourceGen/Program.cs` for the full source-generated path".
