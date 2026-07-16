# Instruction: Add a New Request Handler

## Intent

Add a new `IRequest<T>` + `IRequestHandler<,>` pair to a consumer assembly using the source-generated dispatch path. This is the contract-first flow: define the request record, implement the handler, rely on the MediatorLite source generator to wire everything into `MediatorLiteRegistration`, and verify coverage via `RequestHandlerCount` and a dedicated test.

## When to use

- Adding a new query, command, or void-returning command to a feature module.
- Replacing an ad-hoc service call with a mediator-routed request.
- Porting a handler from a reflection-based pipeline to the source-generated one.

## Agent ownership

- **Primary:** `backend-developer`.
- **Review gate:** `code-reviewer` (serialised after the implementation diff is staged).
- **Test follow-up:** `tester` writes or extends the source-generation test alongside the handler if the handler has non-trivial behaviour.

## Inputs / Preconditions

- You are on a feature branch (not `main`).
- The target consumer project transitively references `MediatorLite` and `MediatorLite.SourceGeneration`. The sample at [samples/MediatorLite.Sample.SourceGen/Program.cs](samples/MediatorLite.Sample.SourceGen/Program.cs) is the canonical reference.
- `dotnet build MediatorLite.sln -c Release` is green on the base branch.
- You understand the async surface split: the public mediator returns `Task`/`Task<T>`; handlers return `ValueTask`/`ValueTask<T>`. See [src/MediatorLite.Abstractions/Abstractions/IRequestHandler.cs](src/MediatorLite.Abstractions/Abstractions/IRequestHandler.cs).

## Numbered steps

1. **Define the request contract as a `record`** in the consumer project. Prefer immutable records; use `IRequest<Unit>` (or the `IRequest` convenience) for void commands. The marker interface hierarchy is fixed:

   ```17:32:src/MediatorLite.Abstractions/Abstractions/IRequest.cs
   public interface IRequest<out TResponse>;

   /// <summary>
   /// Marker interface for requests that don't return a meaningful response.
   /// </summary>
   /// ...
   public interface IRequest : IRequest<Unit>;
   ```

   Example (place in a `Requests/` folder inside your consumer module):

   ```csharp
   public record GetCustomerByIdQuery(int CustomerId) : IRequest<CustomerDto>;
   public record CustomerDto(int Id, string Name, string Email);
   ```

2. **Implement the handler** as a `sealed class` returning `ValueTask<TResponse>`. The handler signature must match `IRequestHandler<TRequest, TResponse>` exactly (see [IRequestHandler.cs](src/MediatorLite.Abstractions/Abstractions/IRequestHandler.cs)).

   ```csharp
   public sealed class GetCustomerByIdQueryHandler
       : IRequestHandler<GetCustomerByIdQuery, CustomerDto>
   {
       private readonly ICustomerRepository _repository;

       public GetCustomerByIdQueryHandler(ICustomerRepository repository)
           => _repository = repository;

       public async ValueTask<CustomerDto> HandleAsync(
           GetCustomerByIdQuery request,
           CancellationToken cancellationToken = default)
       {
           var entity = await _repository.GetAsync(request.CustomerId, cancellationToken);
           return new CustomerDto(entity.Id, entity.Name, entity.Email);
       }
   }
   ```

   For void commands, implement `IRequestHandler<TRequest>` (no second generic argument). The generator lifts the `ValueTask` return into `ValueTask<Unit>`.

3. **Verify DI wiring**. `AddMediatorLite()` takes no arguments and is parameterless by contract; the generator produces `AddGeneratedHandlers()`, which registers the generated `IMediator` itself. `AddMediatorLite()` only adds an optional diagnostic fallback (the generated registration always wins, regardless of call order):

   ```csharp
       public static IServiceCollection AddMediatorLite(this IServiceCollection services)
       {
           services.TryAddScoped<IMediator, ThrowingMediator>();
           return services;
       }
   ```

   Composition root (example from the sample):

   ```csharp
   services.AddGeneratedHandlers();   // from MediatorLite.Generated.MediatorLiteRegistration
   services.AddMediatorLite();         // registers IMediator as Transient
   ```

   If the consumer only needs handlers (not behaviors/validators), use the granular `AddGeneratedRequestHandlers()` instead; see the partial-registration contract in [AGENTS.md](AGENTS.md).

4. **Write a source-generation test** under [tests/MediatorLite.Tests/SourceGeneration/](tests/MediatorLite.Tests/SourceGeneration/). The fixture pattern is already established — put the test DTO/handler in `TestTypes.cs` and add a fact to `MediatorTests.cs`:

   ```csharp
   [Fact]
   public async Task SendAsync_GetCustomerByIdQuery_ReturnsCustomer()
   {
       var services = new ServiceCollection();
       services.AddGeneratedHandlers();
       services.AddMediatorLite();
       services.AddLogging();

       var provider = services.BuildServiceProvider();
       var mediator = provider.GetRequiredService<IMediator>();

       var result = await mediator.SendAsync(new GetCustomerByIdQuery(42));

       result.Should().NotBeNull();
       result.Id.Should().Be(42);
   }
   ```

5. **Assert the generator picked up the handler** by checking `MediatorLiteRegistration.RequestHandlerCount`. Either add an explicit assertion (`RequestHandlerCount.Should().BeGreaterThan(previousCount)`) or confirm the running count in the sample at startup. The count is emitted by the generator here:

   ```791:791:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
           sb.AppendLine($"        public static int RequestHandlerCount => {requestHandlers.Count};");
   ```

6. **Build & test** from the repo root:

   ```powershell
   dotnet build MediatorLite.sln -c Release
   dotnet test  MediatorLite.sln -c Release --no-build
   ```

   Expected: exit code `0`, all tests green. A new handler should bump `RequestHandlerCount` by exactly one (two if you added both a closed and void handler).

## Validation / Acceptance

- `dotnet build MediatorLite.sln -c Release` returns exit code `0` with no analyzer warnings (warnings are treated as errors per [Directory.Build.props](Directory.Build.props)).
- `dotnet test MediatorLite.sln -c Release --no-build` is green and includes at least one new fact that exercises the new handler end-to-end through `IMediator.SendAsync`.
- `MediatorLiteRegistration.RequestHandlerCount` is strictly greater than before the change in the assembly that hosts the handler.
- No manual `services.AddTransient<IRequestHandler<...>, ...>()` lines were added — the generator owns registration.
- The handler file is `sealed` and the request is a `record` (see [.cursor/rules/00-project-conventions.mdc](.cursor/rules/00-project-conventions.mdc)).

## Handoff / Exit criteria

- Hand back to the `orchestrator` with: the new handler path, the new test path, the delta in `RequestHandlerCount`, and the staged `diff_hash`.
- `code-reviewer` must post a `reviews` row keyed on that `diff_hash` before the change is merged.
- If a non-obvious handler pattern was used (e.g. aggregating multiple repositories, cancellation propagation quirks), record it as a `memory` under `.github/Memories/`.

## Related rules, skills, instructions

- Rules: [.cursor/rules/00-project-conventions.mdc](.cursor/rules/00-project-conventions.mdc), [.cursor/rules/10-dispatch-invariants.mdc](.cursor/rules/10-dispatch-invariants.mdc).
- Abstractions: [IRequest.cs](src/MediatorLite.Abstractions/Abstractions/IRequest.cs), [IRequestHandler.cs](src/MediatorLite.Abstractions/Abstractions/IRequestHandler.cs), [IMediator.cs](src/MediatorLite.Abstractions/Abstractions/IMediator.cs).
- Dispatcher: the generated `SourceGeneratedMediator` (emitted by [HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs)).
- Sample: [samples/MediatorLite.Sample.SourceGen/Program.cs](samples/MediatorLite.Sample.SourceGen/Program.cs).
- Tests: [tests/MediatorLite.Tests/SourceGeneration/MediatorTests.cs](tests/MediatorLite.Tests/SourceGeneration/MediatorTests.cs), [tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs](tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs).
- Agent: [.cursor/agents/orchestrator.md](.cursor/agents/orchestrator.md).
- Related instructions: [add-new-pipeline-behavior.md](add-new-pipeline-behavior.md), [add-new-validator.md](add-new-validator.md), [bug-fix-workflow.md](bug-fix-workflow.md).
