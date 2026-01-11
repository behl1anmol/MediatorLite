# MediatorLite

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)

A lightweight, high-performance mediator library for .NET 10+. Built from the ground up with source generators to minimize boilerplate and maximize performance.

## ✨ Features

- **🚀 High Performance** - Source generators eliminate runtime reflection
- **📦 Lightweight** - Minimal dependencies, focused core
- **🔧 Extensible** - Pipeline behaviors for cross-cutting concerns
- **📨 Request/Response** - Type-safe command and query handling
- **📢 Notifications** - Pub-sub pattern with controlled execution
- **🔍 Observable** - Built-in logging and OpenTelemetry support
- **💉 DI Native** - First-class Microsoft.Extensions.DependencyInjection integration

## 📦 Installation

```bash
dotnet add package MediatorLite
```

## 🚀 Quick Start

### 1. Define a Request and Handler

```csharp
// Define a request
public record GetUserQuery(int Id) : IRequest<User>;

// Define the handler
public class GetUserQueryHandler : IRequestHandler<GetUserQuery, User>
{
    public ValueTask<User> HandleAsync(GetUserQuery request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new User { Id = request.Id, Name = "John Doe" });
    }
}
```

### 2. Register Services

```csharp
// Program.cs
services.AddMediatorLite(options =>
{
    options.RegisterHandlersFromAssembly(typeof(Program).Assembly);
});
```

### 3. Send Requests

```csharp
public class MyService(IMediator mediator)
{
    public async Task<User> GetUserAsync(int id, CancellationToken ct)
    {
        return await mediator.SendAsync(new GetUserQuery(id), ct);
    }
}
```

## 📖 Documentation

- [Quick Start Guide](docs/quick-start.md)
- [Pipeline Behaviors](docs/pipeline-behaviors.md)
- [Notifications](docs/notifications.md)
- [Migration from MediatR](docs/migration-from-mediatr.md)

## 🎯 Why MediatorLite?

| Feature | MediatorLite | MediatR |
|---------|-------------|---------|
| Runtime Reflection | ❌ None | ✅ Yes |
| Source Generators | ✅ Yes | ❌ No |
| ValueTask Support | ✅ Native | ❌ Task only |
| OpenTelemetry | ✅ Built-in | ❌ Manual |
| License | MIT | Commercial |

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
