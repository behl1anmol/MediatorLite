using MediatorLite;
using MediatorLite.Sample.Requests;
using MediatorLite.Sample.Handlers;
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

// Register handlers manually
services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserQueryHandler>();
services.AddTransient<IRequestHandler<DeleteUserCommand, Unit>, DeleteUserCommandHandler>();
services.AddTransient<IRequestHandler<CreateOrderCommand, int>, CreateOrderCommandHandler>();

// Register notification handlers
services.AddTransient<INotificationHandler<UserCreatedNotification>, SendWelcomeEmailHandler>();
services.AddTransient<INotificationHandler<UserCreatedNotification>, CreateAuditLogHandler>();

// Register open generic behaviors
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// Add MediatorLite
services.AddMediatorLite(options =>
{
    options.EnableBuiltInLogging = true;
    options.EnableTracing = true;
});

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
