# Instruction: Add a New Validator

## Intent

Attach validation to a request type. MediatorLite supports two orthogonal, auto-registered validation paths: **`System.ComponentModel.DataAnnotations`** attributes (for simple field-level rules) and **`IValidator<T>`** implementations (for business rules or rules that need DI). Both are discovered by the source generator; the generator emits `ValidationBehavior<,>` first in the pipeline for any validated request type.

## When to use

- Field-level constraints: `[Required]`, `[StringLength]`, `[Range]`, `[EmailAddress]`, `[RegularExpression]` — use **DataAnnotations**.
- Cross-field rules, database lookups, async checks, or anything needing DI — use **`IValidator<T>`**.
- Both at once: the generator runs DataAnnotations first, then custom validators, on the same request.

## Agent ownership

- **Primary:** `backend-developer`.
- **Review gate:** `code-reviewer` (make sure validation is the right layer; business-rule logic should not leak into handlers).
- **Tester:** extends [tests/MediatorLite.Tests/SourceGeneration/ValidationTests.cs](tests/MediatorLite.Tests/SourceGeneration/ValidationTests.cs).

## Inputs / Preconditions

- The request is an `IRequest<T>` or `IRequest` already wired via source generation (see [add-new-request-handler.md](add-new-request-handler.md)).
- You know where `ValidationBehavior<,>` lives: `src/MediatorLite/Validation/Validation.cs` (auto-registered by the generator for validated types).
- You understand the pipeline ordering guarantee: validation runs **first**, regardless of `[BehaviorOrder]` on other behaviors.

## Numbered steps

1. **Decision tree — pick one or both paths**:

   | Rule shape                                        | Path                          |
   |---------------------------------------------------|-------------------------------|
   | Single-property, no DI, declarative               | `[DataAnnotations]` on record |
   | Needs repository / async DB call                  | `IValidator<T>`               |
   | Cross-property logic (e.g. StartDate < EndDate)   | `IValidator<T>`               |
   | Enum range, string length, non-empty, email fmt   | `[DataAnnotations]`           |
   | Restricted value lists loaded from config         | `IValidator<T>` (DI the config) |

2. **DataAnnotations path** — decorate the request record. The generator auto-registers `DataAnnotationsValidator<T>` for any request with at least one annotation:

   ```422:430:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
   public sealed record ValidatedCommand : IRequest<string>
   {
       [Required(ErrorMessage = "Name is required")]
       [StringLength(50, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 50 characters")]
       public required string Name { get; init; }

       [Range(1, 100, ErrorMessage = "Value must be between 1 and 100")]
       public int Value { get; init; }
   }
   ```

   No code changes beyond the record are needed — the generator wires the validator.

3. **`IValidator<T>` path** — implement a `sealed class` in the consumer project. The contract is part of the `MediatorLite.Validation` namespace:

   ```450:467:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
   public class ValidatedCommandCustomValidator : IValidator<ValidatedCommand>
   {
       public static bool WasExecuted { get; set; }
       public static void Reset() => WasExecuted = false;

       public ValueTask<MediatorValidationResult> ValidateAsync(ValidatedCommand request, CancellationToken cancellationToken = default)
       {
           WasExecuted = true;

           if (request.Name.Contains("blocked"))
           {
               return ValueTask.FromResult(MediatorValidationResult.Failure(
                   new ValidationError("Name", "Name cannot contain 'blocked'")));
           }

           return ValueTask.FromResult(MediatorValidationResult.Success);
       }
   }
   ```

   Inject whatever services you need via the constructor (`DbContext`, `IOptions<T>`, etc). The REST API benchmark app shows a DB-backed validator pattern:

   ```7:14:tests/MediatorLite.RestApiBenchmarks/Application/Common/CreateOrderCommandValidator.cs
   public sealed class CreateOrderCommandValidator : IAppValidator<CreateOrderCommand>
   {
       private readonly AppDbContext _dbContext;

       public CreateOrderCommandValidator(AppDbContext dbContext)
       {
           _dbContext = dbContext;
       }
   ```

   (Note: that file uses a project-local `IAppValidator` — the MediatorLite library provides `IValidator<T>` in `MediatorLite.Validation`. Use `IValidator<T>` for MediatorLite's auto-registration.)

4. **Combine both** (optional). A single request can carry DataAnnotations **and** one or more `IValidator<T>` implementations. Generated order: DataAnnotations → custom validators → behaviors → handler. Both paths must pass for the handler to run.

5. **Write the tests** under [tests/MediatorLite.Tests/SourceGeneration/ValidationTests.cs](tests/MediatorLite.Tests/SourceGeneration/ValidationTests.cs). Cover:
   - A valid request succeeds and the handler executed.
   - An invalid request throws `ValidationException` containing the expected `PropertyName` and `ErrorMessage`.
   - For combined paths: DataAnnotations failure prevents the custom validator from running (fail-fast) — mirror the pattern in the existing `ValidationTests`.

6. **Verify the generator picked it up**. `MediatorLiteRegistration.ValidatorCount` increases by one per new `IValidator<T>` **and** by one per request that gains DataAnnotations for the first time:

   ```786:800:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
           var totalValidatorCount = validators.Count + requestTypesWithDataAnnotations.Count;
           var nonValidationBehaviorCount = expandedBehaviors
   ```

7. **Build & test**:

   ```powershell
   dotnet test MediatorLite.sln -c Release --filter FullyQualifiedName~Validation
   ```

   Expected exit code: `0`.

## Validation / Acceptance

- An invalid request throws `MediatorLite.Validation.ValidationException` before the handler runs (verify via a handler-executed flag).
- Valid requests execute the handler exactly once; the validator also executes exactly once per path.
- `MediatorLiteRegistration.ValidatorCount` increased by the expected delta.
- No manual `services.AddTransient<IValidator<X>, Impl>()` lines were added — discovery is generator-driven.
- DataAnnotations are on the request record itself, not on a DTO wrapper.

## Handoff / Exit criteria

- Hand back to the orchestrator: path of the new validator, test file updates, and the delta in `ValidatorCount`.
- If the validator hits I/O (DB, network), call that out in the handoff — `code-reviewer` will check cancellation-token propagation and retry semantics.

## Related rules, skills, instructions

- Rules: [.cursor/rules/00-project-conventions.mdc](.cursor/rules/00-project-conventions.mdc), [.cursor/rules/20-source-generator.mdc](.cursor/rules/20-source-generator.mdc).
- Source: `src/MediatorLite/Validation/Validation.cs`.
- Sample: [samples/MediatorLite.Sample.SourceGen/Program.cs](samples/MediatorLite.Sample.SourceGen/Program.cs) (demos both paths end-to-end).
- Tests: [tests/MediatorLite.Tests/SourceGeneration/ValidationTests.cs](tests/MediatorLite.Tests/SourceGeneration/ValidationTests.cs), [tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs](tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs).
- Benchmark example: [tests/MediatorLite.RestApiBenchmarks/Application/Common/CreateOrderCommandValidator.cs](tests/MediatorLite.RestApiBenchmarks/Application/Common/CreateOrderCommandValidator.cs).
- Agent: [.cursor/agents/orchestrator.md](.cursor/agents/orchestrator.md).
- Related instructions: [add-new-request-handler.md](add-new-request-handler.md), [add-new-pipeline-behavior.md](add-new-pipeline-behavior.md).
