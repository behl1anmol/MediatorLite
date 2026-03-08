# Advanced Patterns — MediatorLite.Sample.SourceGen

## Handler Composition

`PlaceOrderCommandHandler` demonstrates the **handler composition** pattern — a handler that
injects `IMediator` to orchestrate sub-requests and publish notifications within a single
command execution:

```csharp
public sealed class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, OrderResult>
{
    private readonly ILogger<PlaceOrderCommandHandler> _logger;
    private readonly IMediator _mediator;

    public PlaceOrderCommandHandler(ILogger<PlaceOrderCommandHandler> logger, IMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }

    public async ValueTask<OrderResult> HandleAsync(PlaceOrderCommand request, CancellationToken cancellationToken = default)
    {
        // 1. Query product to get price
        var product = await _mediator.SendAsync(new GetProductQuery(request.ProductId), cancellationToken);

        // 2. Calculate total
        var totalAmount = product.Price * request.Quantity;
        var orderId = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";

        // 3. Send void command to update stock
        await _mediator.SendAsync(new UpdateStockCommand(request.ProductId, -request.Quantity), cancellationToken);

        // 4. Publish notification — triggers 3 ordered handlers
        await _mediator.PublishAsync(
            new Notifications.OrderPlacedNotification(orderId, request.ProductId, request.Quantity, request.CustomerEmail, totalAmount),
            cancellationToken);

        return new OrderResult(orderId, totalAmount, DateTime.UtcNow);
    }
}
```

Key points:
- The inner `SendAsync` calls pass through the full pipeline (behaviors execute for sub-requests too)
- The handler uses both `SendAsync` (for queries/commands) and `PublishAsync` (for notifications)
- `UpdateStockCommand` is a void command implementing `IRequest` (not `IRequest<T>`)

## Closed vs Open Behaviors

The sample demonstrates both behavior types side by side.

### Open Generic Behavior — PerformanceLoggingBehavior

Applies to **every** request sent through the mediator. Defined as:

```csharp
public sealed class PerformanceLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
```

- Uses `Stopwatch` to measure elapsed time
- Logs `Debug` on normal completion, `Warning` if >500ms, `Error` on exception
- Always calls `next()` — does not short-circuit

### Closed Behavior — PlaceOrderAuthorizationBehavior

Applies **only** to `PlaceOrderCommand`. Defined as:

```csharp
public sealed class PlaceOrderAuthorizationBehavior
    : IPipelineBehavior<PlaceOrderCommand, OrderResult>
```

- Checks `request.CustomerEmail` contains `@` — throws `InvalidOperationException` if not (short-circuit)
- Warns on large orders (quantity > 100) but does not block
- Calls `next()` after authorization passes

### When each type fires

When `PlaceOrderCommand` is sent, the pipeline is:
1. `ValidationBehavior<PlaceOrderCommand, OrderResult>` (if validators exist — none for this command)
2. `PerformanceLoggingBehavior<PlaceOrderCommand, OrderResult>` (open generic)
3. `PlaceOrderAuthorizationBehavior` (closed)
4. `PlaceOrderCommandHandler.HandleAsync` (the actual handler)

When `GetProductQuery` is sent (e.g., from inside PlaceOrderCommandHandler), only the open
generic behavior fires — the closed authorization behavior does not apply.

## Dual-Layer Validation

`CreateProductCommand` showcases how two validator types work together.

### Layer 1: DataAnnotations (auto-detected)

The source generator detects that `CreateProductCommand` has `System.ComponentModel.DataAnnotations`
attributes and automatically registers `DataAnnotationsValidator<CreateProductCommand>`:

```csharp
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

### Layer 2: Custom IValidator (business rules)

`CreateProductCommandValidator` implements `IValidator<CreateProductCommand>` and checks:

| Rule | Property | Condition | Error |
|------|----------|-----------|-------|
| Restricted names | `Name` | Name is one of: Test, Debug, Admin, System (case-insensitive) | "Product name '{Name}' is restricted and cannot be used" |
| Allowed categories | `Category` | Category not in: Electronics, Clothing, Books, Home & Garden, Sports, Toys | "Category '{Category}' is not valid. Allowed categories: ..." |
| High-value stock | `InitialStock` | Price > $500 AND InitialStock > 1000 | "High-value products (>$500) cannot have initial stock exceeding 1000 units" |

The validator also logs a warning (but does not fail) for products priced > $1000.

Both validators are resolved by `ValidationBehavior<CreateProductCommand, int>` which is
registered FIRST in the pipeline. If either validator returns errors, `ValidationException`
is thrown with all accumulated `ValidationError` objects — the handler never executes.

### Validation Demo Scenarios in Program.cs

**Demo 4 — Success:**
```csharp
new CreateProductCommand
{
    Name = "Gaming Laptop",
    Description = "High-performance laptop for gaming and productivity",
    Price = 1299.99m,
    InitialStock = 50,
    Category = "Electronics"
}
// Result: Product created with auto-incremented ID
```

**Demo 5 — DataAnnotation violations:**
```csharp
new CreateProductCommand
{
    Name = "AB",           // StringLength min 3 violation
    Description = "",      // Required violation
    Price = -10m,          // Range min 0.01 violation
    InitialStock = 20000,  // Range max 10000 violation
    Category = "Electronics"
}
// Result: ValidationException with 4 errors from DataAnnotationsValidator
```

**Demo 6 — Business rule violations:**
```csharp
new CreateProductCommand
{
    Name = "Test Product",        // "Test" is a restricted name — wait, actually name is "Test Product"
    Description = "This is a test product",
    Price = 999m,
    InitialStock = 100,
    Category = "InvalidCategory"  // Not in allowed categories
}
// Result: ValidationException with errors from CreateProductCommandValidator
// Note: Name "Test Product" is NOT restricted because RestrictedNames.Contains checks
// the full string — only exact matches like "Test" are blocked
```

Each demo wraps the `SendAsync` call in a try/catch for `ValidationException` and iterates
`ex.Errors` displaying `PropertyName`, `ErrorMessage`, and optionally `AttemptedValue`.

## Anti-Pattern: Double Registration with AddOpenBehavior

When using `AddGeneratedHandlers()`, the source generator automatically registers all discovered
`IPipelineBehavior` implementations — both open generic and closed. Do **not** additionally call
`options.AddOpenBehavior()` in `AddMediatorLite()`, as this would register the behavior twice
and cause it to execute twice in the pipeline.

```csharp
// CORRECT — source generator handles all behavior registration
services.AddGeneratedHandlers();
services.AddMediatorLite(options =>
{
    options.EnableBuiltInLogging = true;
    options.EnableTracing = true;
    // Do NOT add: options.AddOpenBehavior(typeof(PerformanceLoggingBehavior<,>));
});

// WRONG — causes PerformanceLoggingBehavior to run twice per request
services.AddGeneratedHandlers();
services.AddMediatorLite(options =>
{
    options.AddOpenBehavior(typeof(PerformanceLoggingBehavior<,>));  // ❌ double registration
});
```

This anti-pattern applies to `AddBehavior<T>()` as well — do not manually register behaviors
that the source generator already discovers.
