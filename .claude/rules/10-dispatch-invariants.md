# Dispatch Invariants

MediatorLite v2 has one and only one dispatch path: the compile-time
`ISourceGeneratedMediator`. These invariants keep that contract honest.

## Rule 1 — No reflection fallback in `Mediator.cs`

`Mediator` must resolve every request and notification through
`ISourceGeneratedMediator`. Reintroducing reflection, assembly scanning, or
`MakeGenericType` in this class is a breaking regression.

The dispatcher lookup is the only allowed shape:

```34:50:src/MediatorLite/Internal/Mediator.cs
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var dispatcher = _sourceGeneratedMediator.GetDispatcher(requestType)
            ?? throw new InvalidOperationException(
                $"No handler registered for request type {requestType.FullName}. " +
                $"Ensure a handler implementing IRequestHandler<{requestType.Name}, {typeof(TResponse).Name}> " +
                "is registered and AddGeneratedHandlers() is called.");

        var result = await dispatcher(_serviceProvider, request, cancellationToken).ConfigureAwait(false);
        return (TResponse)result;
    }
```

If a new feature seems to require reflection at dispatch time, push the work
into the source generator instead.

## Rule 2 — `ISourceGeneratedMediator` is mandatory

`Mediator` constructor-injects `ISourceGeneratedMediator` and throws if it is
missing. Never make this optional or provide a null-object default inside
`Mediator`. Consumers are expected to call `AddGeneratedHandlers()` (which
registers the generated implementation) before `AddMediatorLite()`.

The contract is small by design:

```63:96:src/MediatorLite.Abstractions/Abstractions/ISourceGeneratedMediator.cs
public interface ISourceGeneratedMediator
{
    /// ...
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    RequestDispatcher? GetDispatcher(Type requestType);

    /// ...
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    NotificationPublisher? GetPublisher(Type notificationType);
}
```

Do not add new methods to this interface without a matching code path in the
generator and a lesson file under `.github/Memories/`.

## Rule 3 — `AddMediatorLite()` is argument-free and Transient

```37:41:src/MediatorLite/Configuration/ServiceCollectionExtensions.cs
    public static IServiceCollection AddMediatorLite(this IServiceCollection services)
    {
        services.AddTransient<IMediator, Mediator>();
        return services;
    }
```

- No overloads. No `Action<MediatorOptions>`. No assembly-scanning overload.
- `IMediator` is always `Transient`. Do not change to `Singleton`/`Scoped`;
  handler and behavior lifetimes are the consumer's concern via DI.
- Consumer call order is fixed: `AddGeneratedHandlers()` then
  `AddMediatorLite()`.

## Rule 4 — Boxing tradeoff is intentional

`RequestDispatcher` returns `Task<object>` by design; value-type responses are
boxed once per dispatch. Do not "fix" this by adding a generic delegate table
— that would force per-type dictionary instantiation and kill the O(1)
dispatch property.
