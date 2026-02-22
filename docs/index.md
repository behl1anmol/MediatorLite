---
title: Home
layout: home
nav_order: 1
---

# MediatorLite

[![CI](https://github.com/behl1anmol/MediatorLite/actions/workflows/ci.yml/badge.svg)](https://github.com/behl1anmol/MediatorLite/actions/workflows/ci.yml)
[![MediatorLite](https://img.shields.io/nuget/v/MediatorLite.svg?label=MediatorLite)](https://www.nuget.org/packages/MediatorLite/)
[![MediatorLite.SourceGeneration](https://img.shields.io/nuget/v/MediatorLite.SourceGeneration.svg?label=MediatorLite.SourceGeneration)](https://www.nuget.org/packages/MediatorLite.SourceGeneration/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)

A lightweight, high-performance mediator for .NET with **zero-reflection dispatch** and **compile-time source generation**.
{: .fs-6 .fw-300 }

[Get Started]({{ site.baseurl }}/quick-start){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[View on GitHub](https://github.com/behl1anmol/MediatorLite){: .btn .fs-5 .mb-4 .mb-md-0 }

---

## Why MediatorLite?

MediatorLite implements the [Mediator pattern](https://en.wikipedia.org/wiki/Mediator_pattern) for .NET applications, decoupling request senders from their handlers. Unlike other mediator libraries, MediatorLite uses **Roslyn source generators** to discover and register handlers at compile time — no runtime reflection required.

| Feature | MediatorLite | Traditional Mediators |
|---|---|---|
| Handler discovery | Compile-time | Runtime reflection |
| Dispatch overhead | Near-zero | Reflection-based |
| Native AOT support | Yes | Limited |
| Assembly trimming | Yes | Limited |
| Startup cost | None | Assembly scanning |

---

## Key Features

- **Zero-Reflection Dispatch** — Handler registration and dispatch are generated at compile time via Roslyn source generators.
- **High Performance** — `ValueTask`-based handlers with minimal overhead and no boxing.
- **Pipeline Behaviors** — Composable middleware for cross-cutting concerns (logging, validation, caching, etc.).
- **Notifications** — Pub/sub pattern with configurable execution strategies: `Sequential`, `Parallel`, and `StopOnFirst`.
- **Built-in Validation** — First-class support for `DataAnnotations` and custom `IValidator<T>` implementations.
- **Observability** — Structured logging and OpenTelemetry tracing out of the box.
- **DI Native** — Integrates directly with `Microsoft.Extensions.DependencyInjection`.
- **Native AOT & Trimming** — Works with .NET's native AOT compilation and assembly trimming.

---

## Installation

Install the core library plus the optional source generator:

```bash
dotnet add package MediatorLite
dotnet add package MediatorLite.SourceGeneration   # Recommended
```

Or for manual registration only:

```bash
dotnet add package MediatorLite
```

---

## Quick Example

**1. Define a request and handler:**

```csharp
public record GetUserQuery(int Id) : IRequest<User>;

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, User>
{
    public async ValueTask<User> HandleAsync(
        GetUserQuery request,
        CancellationToken cancellationToken = default)
    {
        return await _userRepository.GetByIdAsync(request.Id, cancellationToken);
    }
}
```

**2. Register services (source-generated):**

```csharp
services
    .AddGeneratedHandlers()
    .AddMediatorLite();
```

**3. Send a request:**

```csharp
var user = await mediator.SendAsync(new GetUserQuery(42));
```

That's it. The source generator discovers `GetUserQueryHandler` at compile time — no attributes, no assembly scanning.

---

## Documentation

| Page | Description |
|---|---|
| [Quick Start]({{ site.baseurl }}/quick-start) | Install the library and set up your first request, handler, and notification in minutes. |
| [Pipeline Behaviors]({{ site.baseurl }}/pipeline-behaviors) | Compose reusable middleware for logging, validation, caching, and other cross-cutting concerns. |
| [Validation]({{ site.baseurl }}/validation) | Use `DataAnnotations` or custom `IValidator<T>` validator to validate requests before they reach handlers. |
| [Notifications]({{ site.baseurl }}/notifications) | Publish events to multiple handlers with `Sequential`, `Parallel`, or `StopOnFirst` execution strategies. |
| [Observability]({{ site.baseurl }}/observability) | Configure built-in structured logging and OpenTelemetry tracing. |
| [Benchmarks]({{ site.baseurl }}/benchmarks) | Performance comparisons against MediatR across request dispatch, pipeline behaviors, and notifications. |
| [Migrating from MediatR]({{ site.baseurl }}/migration-from-mediatr) | Step-by-step guide and interface mapping for teams moving from MediatR. |
| [Contributing]({{ site.baseurl }}/contributing) | How to build, test, and contribute to MediatorLite. |

---

## License

MediatorLite is released under the [MIT License](https://github.com/behl1anmol/MediatorLite/blob/main/LICENSE).
