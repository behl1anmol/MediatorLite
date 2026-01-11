using MediatorLite;
using MediatorLite.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

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

// ===== Request/Response Types =====

public record GetUserQuery(int Id) : IRequest<User>;
public record User(int Id, string Name, string Email);

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, User>
{
    public ValueTask<User> HandleAsync(GetUserQuery request, CancellationToken cancellationToken = default)
    {
        // Simulate database lookup
        var user = new User(request.Id, "John Doe", "john.doe@example.com");
        return ValueTask.FromResult(user);
    }
}

// ===== Command Types =====

public record DeleteUserCommand(int Id) : IRequest;

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(ILogger<DeleteUserCommandHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask HandleAsync(DeleteUserCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting user with ID: {UserId}", request.Id);
        // Simulate delete operation
        return ValueTask.CompletedTask;
    }
}

public record CreateOrderCommand(
    [property: Required] string ProductName,
    [property: Range(1, 100)] int Quantity,
    [property: Range(0.01, 10000)] decimal Price) : IRequest<int>;

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, int>
{
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(ILogger<CreateOrderCommandHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<int> HandleAsync(CreateOrderCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating order: {Product} x {Quantity} @ {Price:C}",
            request.ProductName, request.Quantity, request.Price);

        // Simulate order creation returning new order ID
        var orderId = Random.Shared.Next(1000, 9999);
        return ValueTask.FromResult(orderId);
    }
}

// ===== Notification Types =====

public record UserCreatedNotification(int UserId, string Email) : INotification;

[NotificationHandlerOrder(1)]
public class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification>
{
    private readonly ILogger<SendWelcomeEmailHandler> _logger;

    public SendWelcomeEmailHandler(ILogger<SendWelcomeEmailHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("   Sending welcome email to {Email}", notification.Email);
        return ValueTask.CompletedTask;
    }
}

[NotificationHandlerOrder(2)]
public class CreateAuditLogHandler : INotificationHandler<UserCreatedNotification>
{
    private readonly ILogger<CreateAuditLogHandler> _logger;

    public CreateAuditLogHandler(ILogger<CreateAuditLogHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask HandleAsync(UserCreatedNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("   Creating audit log for user {UserId}", notification.UserId);
        return ValueTask.CompletedTask;
    }
}

// ===== Pipeline Behaviors =====

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;
        _logger.LogDebug("   [Behavior] Handling {RequestName}", requestName);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        _logger.LogDebug("   [Behavior] Handled {RequestName} in {ElapsedMs}ms", requestName, stopwatch.ElapsedMilliseconds);

        return response;
    }
}
