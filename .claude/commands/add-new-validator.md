# Instruction: Add a New Validator

## Intent

Attach validation to a request type using **FluentValidation**. You write an
`AbstractValidator<TRequest>`; the source generator discovers it and emits
`FluentValidationBehavior<TRequest, TResponse>` as the **outermost** behavior for that request
type. Registration is source-generated — never `AddValidatorsFromAssembly`.

## When to use

- Any field-level or cross-field rule, async/DB-backed check, or business rule — all expressed
  as FluentValidation `RuleFor(...)` rules in an `AbstractValidator<T>`.

> The old in-house `IValidator<T>` / `DataAnnotationsValidator<T>` / `ValidationResult` model
> was removed. Do not use DataAnnotations for validation or implement
> `MediatorLite.Validation.IValidator<T>` (it no longer exists).

## Agent ownership

- **Primary:** `backend-developer`.
- **Review gate:** `code-reviewer` (validation is the right layer; rules shouldn't leak into handlers).
- **Tester:** extends [tests/MediatorLite.Tests/SourceGeneration/ValidationTests.cs](tests/MediatorLite.Tests/SourceGeneration/ValidationTests.cs).

## Inputs / Preconditions

- The request is an `IRequest<T>` / `IRequest` already wired via source generation.
- The consuming project references the **`MediatorLite.FluentValidation`** package (and the
  `FluentValidation` package transitively). Missing it ⇒ generator error **`MEDL1001`**.
- You understand the ordering guarantee: validation runs **outermost**, before any
  `[BehaviorOrder]` behavior and the handler.

## Numbered steps

1. **Write the validator** as an `AbstractValidator<TRequest>` in the assembly that calls
   `AddGeneratedHandlers()`:

   ```csharp
   using FluentValidation;

   public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
   {
       public CreateProductCommandValidator()
       {
           RuleFor(x => x.Name).NotEmpty().Length(3, 100);
           RuleFor(x => x.Price).InclusiveBetween(0.01m, 10000m);
           RuleFor(x => x.Category).Must(BeAllowed).WithMessage("Category is not allowed");
       }

       private static bool BeAllowed(string category) => /* ... */ true;
   }
   ```

   Inject dependencies (e.g. `DbContext`, `IOptions<T>`) via the constructor for async rules
   (`MustAsync(...)`). Validators resolve from the request scope, so scoped deps are fine.

2. **No registration code.** `AddGeneratedHandlers()` discovers the validator and wires
   `FluentValidationBehavior`. Do **not** add `services.AddTransient<IValidator<X>, Impl>()` or
   `AddValidatorsFromAssembly(...)`.

3. **Write tests** under [ValidationTests.cs](tests/MediatorLite.Tests/SourceGeneration/ValidationTests.cs):
   - A valid request succeeds and the handler executed.
   - An invalid request throws `MediatorLite.Validation.ValidationException` with the expected
     `PropertyName`/`ErrorMessage` (FluentValidation failures are mapped to `ValidationError`).
   - Optionally assert validation is outermost (an invalid request short-circuits other
     behaviors). For isolated behavior tests see
     [FluentValidationBehaviorTests.cs](tests/MediatorLite.Tests/UnitTests/FluentValidationBehaviorTests.cs).

4. **Verify discovery.** `MediatorLiteRegistration.ValidatorCount` increases by one per new
   validator **whose request type has a handler in this compilation** (validators for unhandled
   types are ignored).

5. **Build & test**:

   ```bash
   dotnet test MediatorLite.sln --filter FullyQualifiedName~Validation
   ```

   Expected exit code: `0`.

## Validation / Acceptance

- An invalid request throws `MediatorLite.Validation.ValidationException` before the handler runs.
- Valid requests execute the handler exactly once.
- `MediatorLiteRegistration.ValidatorCount` increased by the expected delta.
- No manual validator/behavior registration and no `AddValidatorsFromAssembly`.
- The `MediatorLite.FluentValidation` package is referenced (no `MEDL1001`).

## Handoff / Exit criteria

- Hand back: path of the new validator, test updates, and the `ValidatorCount` delta.
- If the validator hits I/O, call it out — `code-reviewer` checks cancellation-token propagation.

## Related rules, skills, instructions

- Rule: [.claude/rules/50-validation.md](.claude/rules/50-validation.md).
- Skill: [.claude/skills/mediatorlite-validation/SKILL.md](.claude/skills/mediatorlite-validation/SKILL.md).
- Source: [src/MediatorLite.FluentValidation/FluentValidationBehavior.cs](src/MediatorLite.FluentValidation/FluentValidationBehavior.cs).
- Sample: [samples/MediatorLite.Sample.SourceGen/Validators/CreateProductCommandValidator.cs](samples/MediatorLite.Sample.SourceGen/Validators/CreateProductCommandValidator.cs).
- End-user reference: [docs/validation.md](docs/validation.md).
- Related instructions: [add-new-request-handler.md](add-new-request-handler.md), [add-new-pipeline-behavior.md](add-new-pipeline-behavior.md).
