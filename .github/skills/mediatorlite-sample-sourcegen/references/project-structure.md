# Project Structure — MediatorLite.Sample.SourceGen

## csproj References

The project file (`MediatorLite.Sample.SourceGen.csproj`) references two MediatorLite projects:

```xml
<!-- Core library — provides IMediator, IRequest, IPipelineBehavior, validation types, etc. -->
<ProjectReference Include="..\..\src\MediatorLite\MediatorLite.csproj" />

<!-- Source generator — referenced as Analyzer, not a runtime dependency -->
<ProjectReference Include="..\..\src\MediatorLite.SourceGeneration\MediatorLite.SourceGeneration.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Key settings: `net10.0`, `ImplicitUsings=enable`, `Nullable=enable`, `OutputType=Exe`.

NuGet dependencies:
- `Microsoft.Extensions.DependencyInjection` — DI container
- `Microsoft.Extensions.Logging.Console` — console logging

## Folder Organization

```
samples/MediatorLite.Sample.SourceGen/
├── MediatorLite.Sample.SourceGen.csproj
├── Program.cs
├── Requests/
│   ├── GetProductQuery.cs
│   ├── SearchProductsQuery.cs
│   ├── PlaceOrderCommand.cs
│   ├── CreateProductCommand.cs
│   └── UpdateStockCommand.cs
├── Handlers/
│   ├── GetProductQueryHandler.cs
│   ├── SearchProductsQueryHandler.cs
│   ├── PlaceOrderCommandHandler.cs
│   ├── CreateProductCommandHandler.cs
│   └── UpdateStockCommandHandler.cs
├── Notifications/
│   ├── OrderPlacedNotification.cs
│   ├── OrderConfirmationEmailHandler.cs
│   ├── OrderAuditLogHandler.cs
│   └── InventoryReservationHandler.cs
├── Behaviors/
│   ├── PerformanceLoggingBehavior.cs
│   └── PlaceOrderAuthorizationBehavior.cs
└── Validators/
    └── CreateProductCommandValidator.cs
```

## Complete File Inventory

### Entry Point

| File | Purpose |
|------|---------|
| `Program.cs` | Top-level statements entry point. Configures DI with `AddGeneratedHandlers()` + `AddMediatorLite()`, prints source-gen diagnostic counts, then runs 6 demos: single query, search query, order placement (with notifications), valid product creation, DataAnnotation validation failure, business rule validation failure. |

### Requests (DTOs)

| File | Type | Base Interface | Notes |
|------|------|----------------|-------|
| `GetProductQuery.cs` | `record GetProductQuery(int ProductId)` | `IRequest<Product>` | Also defines `record Product(int Id, string Name, decimal Price, int StockQuantity)` |
| `SearchProductsQuery.cs` | `record SearchProductsQuery(string SearchTerm, int MaxResults = 10)` | `IRequest<IReadOnlyList<Product>>` | Returns collection of `Product` (from GetProductQuery.cs) |
| `PlaceOrderCommand.cs` | `record PlaceOrderCommand(int ProductId, int Quantity, string CustomerEmail)` | `IRequest<OrderResult>` | Also defines `record OrderResult(string OrderId, decimal TotalAmount, DateTime OrderDate)` |
| `CreateProductCommand.cs` | `record CreateProductCommand` | `IRequest<int>` | Uses `required` init properties with DataAnnotation attributes: `[Required]`, `[StringLength]`, `[Range]` on Name, Description, Price, InitialStock, Category |
| `UpdateStockCommand.cs` | `record UpdateStockCommand(int ProductId, int QuantityChange)` | `IRequest` | Void command — `IRequest` implies `IRequest<Unit>` |

### Handlers

| File | Class | Implements | Dependencies | Notes |
|------|-------|------------|--------------|-------|
| `GetProductQueryHandler.cs` | `GetProductQueryHandler` | `IRequestHandler<GetProductQuery, Product>` | `ILogger` | Simulates database lookup, returns constructed `Product` |
| `SearchProductsQueryHandler.cs` | `SearchProductsQueryHandler` | `IRequestHandler<SearchProductsQuery, IReadOnlyList<Product>>` | `ILogger` | Generates `Enumerable.Range` products matching search term |
| `PlaceOrderCommandHandler.cs` | `PlaceOrderCommandHandler` | `IRequestHandler<PlaceOrderCommand, OrderResult>` | `ILogger`, `IMediator` | **Handler composition**: sends `GetProductQuery`, `UpdateStockCommand`, publishes `OrderPlacedNotification` |
| `CreateProductCommandHandler.cs` | `CreateProductCommandHandler` | `IRequestHandler<CreateProductCommand, int>` | `ILogger` | Returns `Interlocked.Increment` product ID from static counter |
| `UpdateStockCommandHandler.cs` | `UpdateStockCommandHandler` | `IRequestHandler<UpdateStockCommand>` | `ILogger` | Void handler — returns `ValueTask.CompletedTask` |

### Notifications

| File | Class | Attribute | Notes |
|------|-------|-----------|-------|
| `OrderPlacedNotification.cs` | `OrderPlacedNotification` | — | `record` implementing `INotification` with OrderId, ProductId, Quantity, CustomerEmail, TotalAmount |
| `OrderConfirmationEmailHandler.cs` | `OrderConfirmationEmailHandler` | `[NotificationHandlerOrder(1)]` | Async handler with `Task.Delay(100)` simulating email send |
| `OrderAuditLogHandler.cs` | `OrderAuditLogHandler` | `[NotificationHandlerOrder(2)]` | Sync handler returning `ValueTask.CompletedTask` |
| `InventoryReservationHandler.cs` | `InventoryReservationHandler` | `[NotificationHandlerOrder(3)]` | Sync handler returning `ValueTask.CompletedTask` |

### Behaviors

| File | Class | Type | Scope |
|------|-------|------|-------|
| `PerformanceLoggingBehavior.cs` | `PerformanceLoggingBehavior<TRequest, TResponse>` | Open generic | All requests — logs `Stopwatch` elapsed time, warns >500ms, logs errors |
| `PlaceOrderAuthorizationBehavior.cs` | `PlaceOrderAuthorizationBehavior` | Closed | `PlaceOrderCommand` only — validates customer email contains `@`, warns on quantity >100, throws `InvalidOperationException` on invalid email |

### Validators

| File | Class | Implements | Notes |
|------|-------|------------|-------|
| `CreateProductCommandValidator.cs` | `CreateProductCommandValidator` | `IValidator<CreateProductCommand>` | Custom business logic: restricted names (`Test`, `Debug`, `Admin`, `System`), allowed categories (`Electronics`, `Clothing`, `Books`, `Home & Garden`, `Sports`, `Toys`), high-value stock limit (>$500 products cannot have >1000 stock) |

**Note:** `DataAnnotationsValidator<CreateProductCommand>` is NOT a file in the project — it is
automatically registered by the source generator because `CreateProductCommand` has DataAnnotation attributes.
