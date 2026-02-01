using MediatorLite;
using MediatorLite.Generated;
using MediatorLite.Sample.SourceGen.Behaviors;
using MediatorLite.Sample.SourceGen.Requests;
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
Console.WriteLine();

// Configure services
var services = new ServiceCollection();

// Add logging
services.AddLogging(builder =>
{
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
});

// Add MediatorLite core services
services.AddMediatorLiteCore(options =>
{
    options.EnableBuiltInLogging = true;
    options.EnableTracing = true;
    options.AddOpenBehavior(typeof(PerformanceLoggingBehavior<,>));
});

// 🎯 Register source-generated handlers (no runtime reflection!)
services.AddGeneratedHandlers();

// Register open generic behavior
services.AddTransient(typeof(PerformanceLoggingBehavior<,>));

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

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("    Demo Complete!");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
