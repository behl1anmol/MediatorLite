---
name: mediatorlite-validation
description: FluentValidation-based validation for MediatorLite. Covers authoring validators with FluentValidation `AbstractValidator<T>`, the source-generator discovery + wiring, the `FluentValidationBehavior<TRequest, TResponse>` pipeline behavior (auto-wired *outermost* for every validated request type, in the opt-in `MediatorLite.FluentValidation` package), the uniform `ValidationException` / `ValidationError` error contract (FluentValidation failures are mapped onto it), the `MEDL1001` missing-package diagnostic, and the hand-registration / `AddValidatorsFromAssembly` anti-patterns. Use when adding validation to a request, diagnosing missing or duplicate errors, or reviewing validation wiring.
triggers: validation, validator, FluentValidation, AbstractValidator, RuleFor, FluentValidationBehavior, ValidationException, ValidationError, AttemptedValue, PropertyName, MEDL1001, MediatorLite.FluentValidation, auto-wired validation, validation error contract
---

# MediatorLite Validation (FluentValidation)

## Purpose

MediatorLite's validation engine is **FluentValidation**. You author validators with
`AbstractValidator<T>`; the source generator discovers them and wires
`FluentValidationBehavior<TRequest, TResponse>` as the **outermost** pipeline behavior for
each validated request type. There is **no** runtime assembly scanning
(`AddValidatorsFromAssembly`) — registration is source-generated. Core `MediatorLite` has no
FluentValidation dependency; the integration lives in the opt-in **`MediatorLite.FluentValidation`**
package.

> The previous in-house model (`MediatorLite.Validation.IValidator<T>`,
> `DataAnnotationsValidator<T>`, `ValidationResult`, core `ValidationBehavior`) was **removed**.
> Do not reference or reintroduce these types.

## When to use

- Adding validation to a new `IRequest<TResponse>` (write an `AbstractValidator<T>`).
- Auditing why a request is/isn't validated.
- Reviewing PRs that hand-register validators/behaviors or call `AddValidatorsFromAssembly` —
  both are smells in the source-gen model.
- Designing how `ValidationException` surfaces at the API boundary (HTTP 400 / `ProblemDetails`).

## Entry points

- Behavior: [FluentValidationBehavior.cs](src/MediatorLite.FluentValidation/FluentValidationBehavior.cs).
- Error contract: [ValidationException.cs](src/MediatorLite.Abstractions/Validation/ValidationException.cs),
  [Models/ValidationError.cs](src/MediatorLite.Abstractions/Validation/Models/ValidationError.cs).
- Generator wiring: [HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs)
  — search `GetValidatorInfo`, `DetermineValidationTargets`, `FluentValidationBehavior`, `MEDL1001`.
- End-user reference: [docs/validation.md](docs/validation.md).
- Always-active rule: [.claude/rules/50-validation.md](.claude/rules/50-validation.md).
- Test fixtures: [tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs](tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs)
  (`ValidatedCommandCustomValidator`) and
  [tests/MediatorLite.Tests/UnitTests/FluentValidationBehaviorTests.cs](tests/MediatorLite.Tests/UnitTests/FluentValidationBehaviorTests.cs).

## How it works

1. **Discovery.** The generator matches concrete `FluentValidation.IValidator<T>`
   implementations (by namespace + interface name, not the type-parameter name) — i.e. your
   `AbstractValidator<T>` subclasses. Open-generic and `[MediatorGeneration(Skip=true)]`
   classes are skipped.
2. **Registration.** `AddGeneratedValidators()` registers each validator against
   `FluentValidation.IValidator<TRequest>` — **only** for request types that have a handler in
   the same compilation. `ValidatorCount` reflects exactly these.
3. **Behavior.** `AddGeneratedBehaviors()` registers `FluentValidationBehavior<T, _>` first,
   and the unrolled pipeline places it **outermost** (before any `[BehaviorOrder]` behavior).
4. **Execution.** The behavior resolves `IEnumerable<FluentValidation.IValidator<TRequest>>`,
   runs every validator via `ValidateAsync`, maps each `ValidationFailure`
   (`PropertyName`/`ErrorMessage`/`AttemptedValue`) to a `ValidationError`, and throws one
   `MediatorLite.Validation.ValidationException` if any failed. On success it calls `next()`.
5. **Guard.** If validators exist for handled types but the `MediatorLite.FluentValidation`
   package is missing, the generator reports **`MEDL1001`** (build error).

## Canonical validator

```csharp
using FluentValidation;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(3, 100);
        RuleFor(x => x.Price).InclusiveBetween(0.01m, 10000m);
        // async / DB-backed rule (validator resolved from the request scope):
        // RuleFor(x => x.Sku).MustAsync((sku, ct) => repo.IsUniqueAsync(sku, ct));
    }
}
```

Registration is automatic via `services.AddGeneratedHandlers().AddMediatorLite();`.

## Common tasks

1. **Add validation to a command** — add an `AbstractValidator<XyzCommand>` in the assembly
   that calls `AddGeneratedHandlers()`; rebuild; verify `MediatorLiteRegistration.ValidatorCount`
   increased. Ensure the `MediatorLite.FluentValidation` package is referenced.
2. **Surface errors at HTTP** — catch `MediatorLite.Validation.ValidationException` in
   exception-mapping middleware; project `ex.Errors` into `ProblemDetails`.
3. **Unit-test a validator** — use FluentValidation's `TestValidate`/`ShouldHaveValidationErrorFor`,
   or test `FluentValidationBehavior` directly (see `FluentValidationBehaviorTests`).

## Pitfalls

- **`AddValidatorsFromAssembly(...)`** — never call it in source-gen consumers; it's a runtime
  reflection scan that defeats the source-gen advantage and double-registers.
- **Hand-registering validators or `FluentValidationBehavior`** — the generator already did it.
- **Validator in a different assembly than `AddGeneratedHandlers()`** — the generator only sees
  the current compilation; the validator won't be wired.
- **Missing package** — produces `MEDL1001`; add `MediatorLite.FluentValidation`.
- **Throwing FluentValidation's native `ValidationException`** — the behavior throws
  MediatorLite's uniform `ValidationException`; don't change the contract.
- **Expecting `[BehaviorOrder]` to move validation** — validation is structurally outermost;
  `[BehaviorOrder]` only orders non-validation behaviors.

## Related

- [docs/validation.md](docs/validation.md) — end-user reference.
- [.claude/rules/50-validation.md](.claude/rules/50-validation.md) — always-active invariants.
- [docs/pipeline-behaviors.md](docs/pipeline-behaviors.md) — ordering of complementary behaviors.
- [FluentValidation docs](https://docs.fluentvalidation.net/).
