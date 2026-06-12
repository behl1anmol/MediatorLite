# Validation Rules

Validation is powered by **FluentValidation** and is opt-in per request type but fully
automated once you add a validator. The source generator owns every wiring decision;
hand-registering validators or behaviors is a smell. The runtime integration lives in the
**`MediatorLite.FluentValidation`** package — core `MediatorLite` has **no** dependency on
FluentValidation.

## Rule 1 — Author validators with `AbstractValidator<T>`

Implement FluentValidation's `AbstractValidator<TRequest>` (or any
`FluentValidation.IValidator<TRequest>`). The generator discovers concrete validators by
matching the `FluentValidation.IValidator<T>` interface (by namespace + name, not the
type-parameter name) and registers them against `FluentValidation.IValidator<TRequest>`.

```csharp
using FluentValidation;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(3, 100);
        RuleFor(x => x.Price).InclusiveBetween(0.01m, 10000m);
    }
}
```

The in-house `IValidator<T>`, `DataAnnotationsValidator<T>`, `ValidationResult`, and the core
`ValidationBehavior` were **removed**. Do not reintroduce them, and do not add DataAnnotations
discovery to the generator.

## Rule 2 — `FluentValidationBehavior<,>` is emitted outermost

For any handled request type that has at least one validator, the generator emits
`MediatorLite.FluentValidation.FluentValidationBehavior<TRequest, TResponse>` as the
**outermost** behavior — before any `[BehaviorOrder]` behavior. The behavior runs every
validator, aggregates all FluentValidation `ValidationFailure`s, maps each to a
`MediatorLite.Validation.ValidationError`, and throws a single
`MediatorLite.Validation.ValidationException`. The uniform exception type is the contract
consumers catch.

Do not alter the "emitted outermost" invariant — it is what guarantees invalid requests
short-circuit before any other behavior or the handler runs. The generator forces validation
to the front of each request's behavior group; `[BehaviorOrder]` only orders the
non-validation behaviors.

## Rule 3 — Do not hand-register validators or the behavior

`AddGeneratedHandlers()` (via `AddGeneratedValidators()` / `AddGeneratedBehaviors()`) already
registers the discovered validators and `FluentValidationBehavior`. Never call
`AddValidatorsFromAssembly(...)` (runtime reflection scan — defeats the source-gen advantage)
or `services.AddTransient<FluentValidation.IValidator<FooCommand>, FooValidator>()` in
source-gen consumers. Duplicates inflate `ValidatorCount`.

Only validators whose request type has a handler **in the same compilation** are registered
and counted; a validator for an unhandled request type has no generated pipeline and is
ignored.

## Rule 4 — Missing package is a build error (`MEDL1001`)

If FluentValidation validators are discovered for handled request types but the
`MediatorLite.FluentValidation` package is not referenced, the generator reports the
`MEDL1001` error rather than emitting code that fails to compile or silently dropping
validation. Keep this guard.

## Rule 5 — Error contract stays in `MediatorLite.Abstractions`

`ValidationException` (`MediatorLite.Validation`) and `ValidationError`
(`MediatorLite.Validation.Models`) live in `MediatorLite.Abstractions` and are
FluentValidation-independent. The `MediatorLite.FluentValidation` package references the
abstractions and performs the failure mapping. Do not move the error types into the
FluentValidation package, and do not throw FluentValidation's native `ValidationException`
from the behavior.
