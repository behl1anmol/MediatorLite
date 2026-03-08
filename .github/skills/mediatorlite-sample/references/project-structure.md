# Project Structure — MediatorLite.Sample

## Complete File Inventory

| File | Purpose |
|------|---------|
| `samples/MediatorLite.Sample/MediatorLite.Sample.csproj` | Console app project file. Targets net10.0 (inherited from `Directory.Build.props`). References `MediatorLite` project and NuGet packages: `Microsoft.Extensions.DependencyInjection 9.0.0`, `Microsoft.Extensions.Logging 9.0.0`, `Microsoft.Extensions.Logging.Console 9.0.0`. Marked `IsPackable=false`. |
| `samples/MediatorLite.Sample/Program.cs` | Top-level entry point. Builds DI container, registers all handlers/behaviors/logging manually, configures `MediatorOptions`, runs four demo scenarios (query, void command, notification, command with behavior). |
| `samples/MediatorLite.Sample/Requests/GetUserQuery.cs` | `public record GetUserQuery(int Id) : IRequest<User>` — A query returning a `User` response. Namespace: `MediatorLite.Sample.Requests`. |
| `samples/MediatorLite.Sample/Requests/User.cs` | `public record User(int Id, string Name, string Email)` — Response DTO for `GetUserQuery`. Namespace: `MediatorLite.Sample.Requests`. |
| `samples/MediatorLite.Sample/Requests/DeleteUserCommand.cs` | `public record DeleteUserCommand(int Id) : IRequest` — Void command (returns `Unit`). Namespace: `MediatorLite.Sample.Requests`. |
| `samples/MediatorLite.Sample/Requests/CreateOrderCommand.cs` | `public record CreateOrderCommand([property: Required] string ProductName, [property: Range(1, 100)] int Quantity, [property: Range(0.01, 10000)] decimal Price) : IRequest<int>` — Command with DataAnnotations returning an order ID. Namespace: `MediatorLite.Sample.Requests`. |
| `samples/MediatorLite.Sample/Handlers/GetUserQueryHandler.cs` | `public class GetUserQueryHandler : IRequestHandler<GetUserQuery, User>` — Returns hardcoded `User(request.Id, "John Doe", "john.doe@example.com")`. No logger dependency. |
| `samples/MediatorLite.Sample/Handlers/DeleteUserCommandHandler.cs` | `public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>` — Uses the void handler shorthand (`IRequestHandler<TRequest>` without response type). Constructor-injected `ILogger<DeleteUserCommandHandler>`. Logs `"Deleting user with ID: {UserId}"`. |
| `samples/MediatorLite.Sample/Handlers/CreateOrderCommandHandler.cs` | `public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, int>` — Logs order details (`Product x Quantity @ Price`), returns `Random.Shared.Next(1000, 9999)`. Constructor-injected `ILogger<CreateOrderCommandHandler>`. |
| `samples/MediatorLite.Sample/Notifications/UserCreatedNotification.cs` | `public record UserCreatedNotification(int UserId, string Email) : INotification` — Published after user creation. |
| `samples/MediatorLite.Sample/Notifications/SendWelcomeEmailHandler.cs` | `[NotificationHandlerOrder(1)] public class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification>` — Logs sending welcome email to `notification.Email`. Runs first due to order 1. |
| `samples/MediatorLite.Sample/Notifications/CreateAuditLogHandler.cs` | `[NotificationHandlerOrder(2)] public class CreateAuditLogHandler : INotificationHandler<UserCreatedNotification>` — Logs creating audit log for `notification.UserId`. Runs second due to order 2. |
| `samples/MediatorLite.Sample/Behaviors/LoggingBehavior.cs` | `public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>` — Open generic behavior. Uses `Stopwatch` to measure elapsed time. Logs `[Behavior] Handling {RequestName}` before and `[Behavior] Handled {RequestName} in {ElapsedMs}ms` after calling `next()`. |

## Folder Organization

```
samples/MediatorLite.Sample/
├── Program.cs                          # Entry point + DI registration
├── MediatorLite.Sample.csproj          # Project file
├── Requests/                           # Request types + response DTOs
│   ├── GetUserQuery.cs                 # IRequest<User>
│   ├── User.cs                         # Response record
│   ├── DeleteUserCommand.cs            # IRequest (void)
│   └── CreateOrderCommand.cs           # IRequest<int> with DataAnnotations
├── Handlers/                           # Request handler implementations
│   ├── GetUserQueryHandler.cs          # IRequestHandler<GetUserQuery, User>
│   ├── DeleteUserCommandHandler.cs     # IRequestHandler<DeleteUserCommand>
│   └── CreateOrderCommandHandler.cs    # IRequestHandler<CreateOrderCommand, int>
├── Notifications/                      # Notification types + handlers
│   ├── UserCreatedNotification.cs      # INotification
│   ├── SendWelcomeEmailHandler.cs      # INotificationHandler (order 1)
│   └── CreateAuditLogHandler.cs        # INotificationHandler (order 2)
└── Behaviors/                          # Pipeline behaviors
    └── LoggingBehavior.cs              # IPipelineBehavior<,> (open generic)
```

## Naming Conventions

| Category | Pattern | Examples |
|----------|---------|----------|
| Queries | `{Entity}Query` | `GetUserQuery` |
| Commands | `{Action}{Entity}Command` | `DeleteUserCommand`, `CreateOrderCommand` |
| Response DTOs | `{Entity}` (plain name) | `User` |
| Request handlers | `{RequestName}Handler` | `GetUserQueryHandler`, `DeleteUserCommandHandler` |
| Notifications | `{Entity}{Event}Notification` | `UserCreatedNotification` |
| Notification handlers | `{Action}Handler` (descriptive verb) | `SendWelcomeEmailHandler`, `CreateAuditLogHandler` |
| Behaviors | `{Purpose}Behavior<TRequest, TResponse>` | `LoggingBehavior<TRequest, TResponse>` |

All request and notification types are **positional records**. All handlers are **classes** (not records, not sealed in the sample).
