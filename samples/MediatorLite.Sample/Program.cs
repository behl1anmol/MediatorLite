using MediatorLite;
using MediatorLite.Sample.Requests;
using MediatorLite.Sample.Notifications;
using MediatorLite.Sample.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Setup DI container
var services = new ServiceCollection();

// Add logging
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// Add MediatorLite with handlers from this assembly
services.AddMediatorLite(options =>
{
    options.RegisterHandlersFromAssemblyContaining<Program>();
    options.AddOpenBehavior(typeof(LoggingBehavior<,>));
    options.EnableBuiltInLogging = true;
    options.EnableTracing = true;
});

// Register open generic behaviors
services.AddTransient(typeof(LoggingBehavior<,>));

// Build provider
var provider = services.BuildServiceProvider();

// Get mediator
var mediator = provider.GetRequiredService<IMediator>();

Console.WriteLine("=== MediatorLite Sample Application ===\n");

// Example 1: Simple Query
Console.WriteLine("1. Sending GetUserQuery...");
var user = await mediator.SendAsync(new GetUserQuery(42));
Console.WriteLine($"   Result: User {{ Id = {user.Id}, Name = {user.Name}, Email = {user.Email} }}\n");

// Example 2: Command with no return value
Console.WriteLine("2. Sending DeleteUserCommand...");
await mediator.SendAsync(new DeleteUserCommand(1));
Console.WriteLine("   Command executed successfully.\n");

// Example 3: Publishing a notification
Console.WriteLine("3. Publishing UserCreatedNotification...");
await mediator.PublishAsync(new UserCreatedNotification(user.Id, user.Email));
Console.WriteLine();

// Example 4: Command that uses pipeline behaviors
Console.WriteLine("4. Sending CreateOrderCommand (with logging behavior)...");
var orderId = await mediator.SendAsync(new CreateOrderCommand("Product A", 2, 99.99m));
Console.WriteLine($"   Result: Order created with ID = {orderId}\n");

Console.WriteLine("=== Sample Complete ===");
