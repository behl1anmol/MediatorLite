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

A lightweight, high-performance mediator for .NET with **O(1) source-generated dispatch** and **compile-time configuration**.
{: .fs-6 .fw-300 }

[Get Started]({{ site.baseurl }}/quick-start){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[View on GitHub](https://github.com/behl1anmol/MediatorLite){: .btn .fs-5 .mb-4 .mb-md-0 }

---

## v2 Architecture

MediatorLite v2 is **source-generation-first**:

- **O(1) dispatch** via compile-time generated switch expressions — no dictionary lookups or reflection
- **Compile-time attributes** control behavior ordering and notification strategies
- **Reflection fallback is deprecated** — manual DI registration still works but uses slower reflection-based dispatch

| Aspect | v1 | v2 |
|--------|----|----|
| **Primary dispatch** | Reflection with caching | O(1) generated switch |
| **Behavior ordering** | DI registration order | `[BehaviorOrder]` attribute |
| **Notification strategies** | `MediatorOptions` runtime | `[NotificationOptions]` compile-time |
| **Reflection fallback** | Supported | Deprecated |

---

## Why MediatorLite?

MediatorLite implements the [Mediator pattern](https://en.wikipedia.org/wiki/Mediator_pattern) for .NET applications, decoupling request senders from their handlers. Unlike other mediator libraries, MediatorLite v2 uses **Roslyn source generators** to generate O(1) dispatch code at compile time.

| Feature | MediatorLite v2 | Traditional Mediators |
|---|---|---|
| Handler dispatch | O(1) switch expression | Reflection/dictionary lookup |
| Handler discovery | Compile-time | Runtime reflection |
| Configuration | Compile-time attributes | Runtime options |
| Native AOT support | Yes | Limited |
| Assembly trimming | Yes | Limited |
| Startup cost | None | Assembly scanning |

---

## Key Features

- **O(1) Dispatch** — Source-generated switch expressions provide constant-time handler resolution.
- **Compile-Time Configuration** — `[BehaviorOrder]`, `[NotificationOptions]`, and `[NotificationHandlerOrder]` attributes control behavior at compile time.
- **High Performance** — `ValueTask`-based handlers with minimal overhead and no boxing.
- **Pipeline Behaviors** — Composable middleware for cross-cutting concerns (logging, validation, caching, etc.).
- **Notifications** — Pub/sub pattern with configurable execution strategies: `Sequential`, `Parallel`, and `StopOnFirst`.
- **Built-in Validation** — First-class support for `DataAnnotations` and custom `IValidator<T>` implementations.
- **Observability** — Structured logging and OpenTelemetry tracing out of the box.
- **DI Native** — Integrates directly with `Microsoft.Extensions.DependencyInjection`.
- **Native AOT & Trimming** — Works with .NET's native AOT compilation and assembly trimming.

---

## Installation

Install the core library plus the source generator (required for v2):

```bash
dotnet add package MediatorLite
dotnet add package MediatorLite.SourceGeneration   # Required for O(1) dispatch
```

> ⚠️ **v2 Note:** Without `MediatorLite.SourceGeneration`, the library falls back to deprecated reflection-based dispatch.

For shared contracts libraries (requests, notifications, and validation contracts only):

```bash
dotnet add package MediatorLite.Abstractions
```

`MediatorLite` already depends on `MediatorLite.Abstractions`, so application projects that install `MediatorLite` get abstractions transitively.

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

**2. Register services (source-generated — must call `AddGeneratedHandlers()` first):**

```csharp
services
    .AddGeneratedHandlers()   // MUST be called first for O(1) dispatch
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
