using MediatorLite;
using MediatorLite.Generated;
using MediatorLite.Sample.SourceGen.Requests;
using MediatorLite.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ═══════════════════════════════════════════════════════════════════════════
// MediatorLite Source Generator Sample
// ═══════════════════════════════════════════════════════════════════════════
// This sample demonstrates how to use source generators for compile-time
// handler discovery, eliminating runtime reflection overhead.
// ═══════════════════════════════════════════════════════════════════════════

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("    MediatorLite Source Generator Sample");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();

// Show source generator stats
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

// Configure services
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

// Build the service provider
var serviceProvider = services.BuildServiceProvider();
var mediator = serviceProvider.GetRequiredService<IMediator>();

Console.WriteLine("───────────────────────────────────────────────────────────────");
Console.WriteLine();

// Demo 1: Query a single product
Console.WriteLine("1️⃣ Querying single product...");
Console.WriteLine();
var product = await mediator.SendAsync(new GetProductQuery(42));
Console.WriteLine($"   Result: {product}");
Console.WriteLine();

// Demo 2: Search for products
Console.WriteLine("───────────────────────────────────────────────────────────────");
Console.WriteLine();
Console.WriteLine("2️⃣ Searching for products...");
Console.WriteLine();
var searchResults = await mediator.SendAsync(new SearchProductsQuery("Laptop", MaxResults: 5));
Console.WriteLine($"   Found {searchResults.Count} products:");
foreach (var p in searchResults)
{
    Console.WriteLine($"   - {p.Name} (${p.Price}, Stock: {p.StockQuantity})");
}

Console.WriteLine();

// Demo 3: Place an order (triggers notifications to multiple handlers)
Console.WriteLine("───────────────────────────────────────────────────────────────");
Console.WriteLine();
Console.WriteLine("3️⃣ Placing an order (triggers notifications)...");
Console.WriteLine();
var orderResult = await mediator.SendAsync(new PlaceOrderCommand(
    ProductId: 42,
    Quantity: 3,
    CustomerEmail: "customer@example.com"));

Console.WriteLine();
Console.WriteLine($"   ✅ Order placed successfully!");
Console.WriteLine($"   Order ID: {orderResult.OrderId}");
Console.WriteLine($"   Total: {orderResult.TotalAmount:C}");
Console.WriteLine($"   Date: {orderResult.OrderDate:g}");
Console.WriteLine();

// Demo 4: Validation - Success case
Console.WriteLine("───────────────────────────────────────────────────────────────");
Console.WriteLine();
Console.WriteLine("4️⃣ Creating a product with validation (SUCCESS)...");
Console.WriteLine();

try
{
    var validProductId = await mediator.SendAsync(new CreateProductCommand
    {
        Name = "Gaming Laptop",
        Description = "High-performance laptop for gaming and productivity",
        Price = 1299.99m,
        InitialStock = 50,
        Category = "Electronics"
    });

    Console.WriteLine($"   ✅ Product created successfully! ID: {validProductId}");
}
catch (ValidationException ex)
{
    Console.WriteLine($"   ❌ Validation failed:");
    foreach (var error in ex.Errors)
    {
        Console.WriteLine($"      - {error.PropertyName}: {error.ErrorMessage}");
    }
}

Console.WriteLine();

// Demo 5: Validation - DataAnnotations failure
Console.WriteLine("───────────────────────────────────────────────────────────────");
Console.WriteLine();
Console.WriteLine("5️⃣ Creating a product with INVALID data (DataAnnotations)...");
Console.WriteLine();

try
{
    var invalidProductId = await mediator.SendAsync(new CreateProductCommand
    {
        Name = "AB",  // Too short (min 3 chars)
        Description = "",  // Required
        Price = -10m,  // Below minimum
        InitialStock = 20000,  // Above maximum
        Category = "Electronics"
    });

    Console.WriteLine($"   ✅ Product created: {invalidProductId}");
}
catch (ValidationException ex)
{
    Console.WriteLine($"   ❌ Validation failed with {ex.Errors.Count} error(s):");
    foreach (var error in ex.Errors)
    {
        Console.WriteLine($"      - {error.PropertyName}: {error.ErrorMessage}");
    }
}

Console.WriteLine();

// Demo 6: Validation - Custom business rules failure
Console.WriteLine("───────────────────────────────────────────────────────────────");
Console.WriteLine();
Console.WriteLine("6️⃣ Creating a product with INVALID business rules...");
Console.WriteLine();

try
{
    var businessRuleViolationId = await mediator.SendAsync(new CreateProductCommand
    {
        Name = "Test Product",  // Restricted name
        Description = "This is a test product",
        Price = 999m,
        InitialStock = 100,
        Category = "InvalidCategory"  // Not in allowed categories
    });

    Console.WriteLine($"   ✅ Product created: {businessRuleViolationId}");
}
catch (ValidationException ex)
{
    Console.WriteLine($"   ❌ Validation failed with {ex.Errors.Count} error(s):");
    foreach (var error in ex.Errors)
    {
        Console.WriteLine($"      - {error.PropertyName}: {error.ErrorMessage}");
        if (error.AttemptedValue != null)
        {
            Console.WriteLine($"        Attempted value: {error.AttemptedValue}");
        }
    }
}

Console.WriteLine();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("    Demo Complete!");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
