# MediatorLite Project Conventions

These rules are non-negotiable and apply to every file in the solution. They
mirror what `Directory.Build.props` enforces at build time and what `AGENTS.md`
documents as the canonical style.

## Build properties (from `Directory.Build.props`)

```1:10:Directory.Build.props
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>
```

- Target framework is `net10.0` only. Do not multi-target or downgrade.
- `<Nullable>enable</Nullable>` — annotate every reference-type parameter and
  return. A `null`-returning API must say so with `?`.
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — a warning breaks the
  build. Fix the code; never `#pragma warning disable` without a comment that
  cites a specific analyzer ID and a reason.
- `<ImplicitUsings>enable</ImplicitUsings>` — don't re-add SDK-implicit
  `using`s like `using System;` or `using System.Threading.Tasks;`.

## Async surface

MediatorLite is `ValueTask`-based end-to-end:

- **Public mediator surface** returns `ValueTask` / `ValueTask<T>` so a
  zero-behavior request whose handler completes synchronously allocates
  nothing. See `IMediator.SendAsync` / `PublishAsync`. Consumers must consume
  the result exactly once; `.AsTask()` is the documented escape hatch for
  `Task.WhenAll` / fan-out / multi-await.
- **Handlers, behaviors, validators, notification handlers** return
  `ValueTask` / `ValueTask<T>` to keep synchronous completion allocation-free.

Concretely:

```55:58:src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs
    ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default);
```

Do not change handler/behavior signatures to `Task<T>`. Do not change
`IMediator` back to `Task<T>` — the zero-allocation fast path depends on
forwarding the handler's `ValueTask` unchanged.

## Void commands

Commands that don't return a value use `IRequest` (which is `IRequest<Unit>`)
and the handler returns `ValueTask`. Do not invent a separate `ICommand`
hierarchy.

```17:32:src/MediatorLite.Abstractions/Abstractions/IRequest.cs
public interface IRequest<out TResponse>;

/// <summary>
/// Marker interface for requests that don't return a meaningful response.
/// </summary>
/// ...
public interface IRequest : IRequest<Unit>;
```

## C# style

- File-scoped namespaces everywhere. Block-scoped namespaces are a code smell
  in new code.
- Prefer `record` / `record struct` for request, response, and notification
  DTOs.
- `sealed` on concrete handlers, behaviors, and validators unless inheritance
  is a deliberate extension point.
- No `Console.WriteLine` outside of `samples/` and benchmarks. Use
  `ILogger<T>` in library code.
- One public type per file in `src/`. Test `TestTypes.cs` files may group
  fixtures.

## DI surface

`AddMediatorLite()` takes no arguments and is parameterless by contract (see
rule `90-public-api-discipline`). Never introduce an `Action<MediatorOptions>`
overload.
