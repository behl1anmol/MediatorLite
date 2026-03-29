# Fairness and Parity Checklist

This checklist defines what must remain identical when comparing MediatorLite and MediatR in this benchmark suite.

## Meaning

- Fairness: both benchmark variants execute the same business/API/database workload so conclusions are attributable to mediator behavior, not environmental drift.
- Parity: concrete equality of critical benchmark conditions (same seeded data, same endpoint contracts, same behavior depth, same notification fan-out, same validation setup).

## Guarded In Startup (hard-fail)

The startup parity guard validates these invariants and throws `BenchmarkParityViolationException` if any check fails.

1. Mediator registration exclusivity:
   - MediatorLite mode: `MediatorLite.IMediator` present and `MediatR.IMediator` absent.
   - MediatR mode: `MediatR.IMediator` present and `MediatorLite.IMediator` absent.
2. Pipeline depth parity for `CreateOrderCommand`:
   - 3 behaviors on each stack (validation, logging, metrics).
3. Notification fan-out parity:
   - 3 `OrderCreatedNotification` handlers on each stack.
4. Validation parity:
   - exactly 1 `IAppValidator<CreateOrderCommand>`.
5. Seeded dataset parity:
   - Medium profile: 800 customers, 1200 products, 6000 orders, 10000 audit entries.
   - Large profile: 2500 customers, 4000 products, 18000 orders, 25000 audit entries.

## Maintainer Checklist

1. Keep endpoint routes and payload shapes identical for both mediator modes.
2. Keep EF Core and SQLite configuration identical for both mediator modes.
3. Keep transport mode selection independent from mediator selection.
4. Keep benchmark methods side-effect comparable (same request templates and IDs).
5. If behavior count or handler graph changes intentionally, update:
   - this checklist
   - `BenchmarkParityGuard`
   - benchmark interpretation notes in docs.
