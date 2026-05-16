---
activation: always
---

# Public API Discipline

`MediatorLite.Abstractions` is the consumer-facing surface. Everything in
there is an API contract that downstream packages depend on. Changes are
governed, not free.

## Rule 1 — Abstractions live in `MediatorLite.Abstractions`

Public interfaces, records, attributes, delegates, and enums belong in
`src/MediatorLite.Abstractions/`. Do not add public types to
`src/MediatorLite/` except for the DI entry point
(`ServiceCollectionExtensions`) and narrowly-scoped helpers like
`MediatorActivitySource` that consumers may reference for instrumentation.

Current abstraction surface (do not reorganize without a memory note):

- `Abstractions/IMediator.cs`
- `Abstractions/IRequest.cs`, `IRequestHandler.cs`
- `Abstractions/INotification.cs`, `INotificationHandler.cs`
- `Abstractions/IPipelineBehavior.cs`
- `Abstractions/ISourceGeneratedMediator.cs`
- `Abstractions/Attributes.cs`
- `Abstractions/Unit.cs`
- `Validation/IValidator.cs`, `Validation/ValidationException.cs`,
  `Validation/Models/*.cs`

## Rule 2 — `AddMediatorLite()` signature is frozen

```37:41:src/MediatorLite/Configuration/ServiceCollectionExtensions.cs
    public static IServiceCollection AddMediatorLite(this IServiceCollection services)
    {
        services.AddTransient<IMediator, Mediator>();
        return services;
    }
```

The argument-free overload is the entire API. Do not add:

- `AddMediatorLite(Action<MediatorOptions> configure)`
- `AddMediatorLite(params Assembly[] assemblies)`
- `AddMediatorLite(ServiceLifetime lifetime)`

Each one of these re-introduces the exact runtime-configuration sprawl the
v2 redesign deleted. The companion `MediatorOptions` class was removed and
must not come back.

## Rule 3 — Breaking changes require an ADR + a lesson

A breaking change is any of:

- Removing a public type or member
- Changing a public method/delegate signature
- Changing the semantics of an attribute (its precedence, its inheritance,
  its target)
- Renaming anything under `MediatorLite.Generated.MediatorLiteRegistration`
  (including the `*Count` constants)

Every breaking change needs:

1. A short ADR under `.github/Memories/<slug>.md` recording the decision,
   the alternatives considered, and the consumer migration path.
2. A lesson file under `.github/Lessons/` if the break was driven by a bug
   that should not recur.
3. An entry in the migration doc — the repo already has
   `docs/migration-v1-to-v2.md`; extend it rather than starting a new file.

## Rule 4 — `[Obsolete]` is a public-API contract, not a cleanup tool

Types kept for back-compat (e.g. `MediatorGenerationAttribute`) must stay in
place with their `[Obsolete]` attribute and documented deprecation message.
Do not delete them outside of an explicit major-version bump.

```216:225:src/MediatorLite.Abstractions/Abstractions/Attributes.cs
[Obsolete("This attribute is no longer valid with the complete source generator implementation.")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MediatorGenerationAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether to skip source generation for this type.
    /// Default is false.
    /// </summary>
    public bool Skip { get; set; }
}
```

If you genuinely need to delete an obsolete symbol, it is a Rule 3 breaking
change.

## Rule 5 — Internal types stay internal

`src/MediatorLite/Internal/Mediator.cs` is `internal sealed`. Do not expose
it publicly, do not subclass it in consumers. The only public entry is
`IMediator`, which is resolved from DI.

## Rule 6 — Generated namespace is reserved

`MediatorLite.Generated` is owned by the source generator. Do not hand-write
types in that namespace in `src/` or in tests. Consumer code `using
MediatorLite.Generated;` purely to call `AddGeneratedHandlers()` is the only
supported interaction.
