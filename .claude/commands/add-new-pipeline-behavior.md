# Instruction: Add a New Pipeline Behavior

## Intent

Add a cross-cutting `IPipelineBehavior<TRequest, TResponse>` that wraps one or more request handlers. Decide between an **open generic** behavior (applies to every request) and a **closed generic** behavior (applies to one specific request/response pair), set ordering via `[BehaviorOrder]`, and confirm the behavior participates in the generated pipeline for the right subset of request types.

## When to use

- Adding logging, tracing, caching, authorization, metrics, retry, transaction, or audit cross-cutting concerns.
- Enforcing per-request invariants that are not business validation (for that, see [add-new-validator.md](add-new-validator.md)).
- Implementing a short-circuit guard (feature flag, idempotency key check) that returns early without calling `next()`.

## Agent ownership

- **Primary:** `backend-developer`.
- **Review gate:** `code-reviewer` (ordering, short-circuit semantics, allocation impact).
- **Benchmark follow-up:** `devops` runs [write-and-run-benchmarks.md](write-and-run-benchmarks.md) if the behavior is in the hot path.

## Inputs / Preconditions

- You understand the pipeline contract: behaviors are executed in `[BehaviorOrder]` order (lower first), wrapping the handler. Validation behaviors are emitted by the generator **before** non-validation behaviors for validated request types. See [AGENTS.md](AGENTS.md).
- The behavior signature matches [IPipelineBehavior.cs](src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs) exactly:

  ```45:59:src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs
  public interface IPipelineBehavior<in TRequest, TResponse>
      where TRequest : IRequest<TResponse>
  {
      /// <summary>
      /// Handles the request by optionally performing work before/after invoking the next handler.
      /// </summary>
      /// <param name="request">The request being handled.</param>
      /// <param name="next">The delegate to invoke the next behavior or the actual handler.</param>
      /// <param name="cancellationToken">Cancellation token for the operation.</param>
      /// <returns>A <see cref="ValueTask{TResponse}"/> representing the response.</returns>
      ValueTask<TResponse> HandleAsync(
          TRequest request,
          RequestHandlerDelegate<TResponse> next,
          CancellationToken cancellationToken = default);
  }
  ```

## Numbered steps

1. **Pick open vs closed generic**:
   - **Open generic** (`class Xyz<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>`) — applies to every request type in the assembly. Use for logging/metrics/tracing.
   - **Closed generic** (`class Xyz : IPipelineBehavior<SpecificCommand, SpecificResult>`) — applies only to `SpecificCommand`. Use for request-specific authorization, idempotency, or transactional boundaries.

2. **Choose `[BehaviorOrder]`**. Lower values wrap outer; higher values wrap inner (closer to the handler). The existing conventions from the sample and tests:
   - Authorization / short-circuit: `0`–`10`.
   - Logging / tracing: `20`–`40`.
   - Caching: `50`–`60`.
   - Metrics / audit: `70`–`90`.

   The generator picks up the attribute declaratively; see the test fixtures:

   ```342:353:tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs
   [BehaviorOrder(1)]
   public class AddOneBehavior : IPipelineBehavior<ComputeValueQuery, int>
   {
       public async ValueTask<int> HandleAsync(
           ComputeValueQuery request,
           RequestHandlerDelegate<int> next,
           CancellationToken cancellationToken = default)
       {
           var result = await next();
           return result + 1;
       }
   }
   ```

   > Note: validation behaviors emitted by the generator always run first for validated request types, regardless of `[BehaviorOrder]` on other behaviors. See [.claude/rules/20-source-generator.mdc](.claude/rules/20-source-generator.mdc).

3. **Implement the behavior**. Open generic skeleton:

   ```csharp
   [BehaviorOrder(30)]
   public sealed class LoggingBehavior<TRequest, TResponse>
       : IPipelineBehavior<TRequest, TResponse>
       where TRequest : IRequest<TResponse>
   {
       private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

       public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
           => _logger = logger;

       public async ValueTask<TResponse> HandleAsync(
           TRequest request,
           RequestHandlerDelegate<TResponse> next,
           CancellationToken cancellationToken = default)
       {
           _logger.LogInformation("Handling {RequestType}", typeof(TRequest).Name);
           var response = await next();
           _logger.LogInformation("Handled {RequestType}", typeof(TRequest).Name);
           return response;
       }
   }
   ```

4. **Short-circuit (optional)**. A behavior may return without calling `next()`; the handler and any inner behaviors are skipped. Use this for authorization failures or cached-result lookups.

   ```csharp
   [BehaviorOrder(5)]
   public sealed class IdempotencyBehavior : IPipelineBehavior<CreateOrderCommand, OrderResult>
   {
       public ValueTask<OrderResult> HandleAsync(
           CreateOrderCommand request,
           RequestHandlerDelegate<OrderResult> next,
           CancellationToken cancellationToken = default)
       {
           if (_cache.TryGet(request.IdempotencyKey, out var cached))
           {
               return ValueTask.FromResult(cached);   // next() not called — short-circuit
           }
           return next();
       }
   }
   ```

5. **Write two tests** under [tests/MediatorLite.Tests/SourceGeneration/PipelineBehaviorTests.cs](tests/MediatorLite.Tests/SourceGeneration/PipelineBehaviorTests.cs):
   - A **wrapping test** that asserts the behavior ran before and after `next()`.
   - A **short-circuit test** that asserts downstream handlers/behaviors were not executed when the behavior returns without calling `next()`. Use the `ShortCircuitBehavior` / `ShortCircuitLoggerBehavior` pattern already in [TestTypes.cs](tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs) as a reference.

6. **Verify `BehaviorCount` increments**. The generator emits the count here:

   ```797:797:src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs
           sb.AppendLine($"        public static int BehaviorCount => {nonValidationBehaviorCount + requestTypesWithValidation.Count};");
   ```

7. **Build & test**:

   ```powershell
   dotnet build MediatorLite.sln -c Release
   dotnet test  MediatorLite.sln -c Release --no-build --filter FullyQualifiedName~PipelineBehavior
   ```

   Expected exit code: `0`.

## Validation / Acceptance

- The behavior participates in the pipeline only where expected: open generic applies to all requests; closed generic applies only to the declared request type.
- `[BehaviorOrder]` produces the documented execution order (verify with a tracking behavior if in doubt).
- Short-circuit behaviors must have a dedicated test asserting inner handlers/behaviors do **not** run.
- `MediatorLiteRegistration.BehaviorCount` increased by exactly one per new non-validation behavior.
- No runtime reflection was introduced; the generated typed-switch dispatch (emitted by [HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs)) is untouched.

## Handoff / Exit criteria

- Report to the orchestrator: path of the new behavior, new test names, ordering choice, and whether it can short-circuit.
- If the behavior affects hot-path latency or allocations (e.g. new logger scopes, new `ActivitySource` calls), trigger [write-and-run-benchmarks.md](write-and-run-benchmarks.md) before merge.
- `code-reviewer` signs off on ordering semantics and short-circuit correctness.

## Related rules, skills, instructions

- Rules: [.claude/rules/10-dispatch-invariants.mdc](.claude/rules/10-dispatch-invariants.mdc), [.claude/rules/20-source-generator.mdc](.claude/rules/20-source-generator.mdc).
- Abstractions: [IPipelineBehavior.cs](src/MediatorLite.Abstractions/Abstractions/IPipelineBehavior.cs), [Attributes.cs](src/MediatorLite.Abstractions/Abstractions/Attributes.cs).
- Samples & tests: [samples/MediatorLite.Sample.SourceGen/Program.cs](samples/MediatorLite.Sample.SourceGen/Program.cs), [tests/MediatorLite.Tests/SourceGeneration/PipelineBehaviorTests.cs](tests/MediatorLite.Tests/SourceGeneration/PipelineBehaviorTests.cs), [tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs](tests/MediatorLite.Tests/SourceGeneration/TestTypes.cs).
- Benchmarks: [tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs](tests/MediatorLite.Benchmarks/MediatorBenchmarks.cs).
- Agent: [.claude/agents/orchestrator.md](.claude/agents/orchestrator.md).
- Related instructions: [add-new-request-handler.md](add-new-request-handler.md), [add-new-validator.md](add-new-validator.md), [write-and-run-benchmarks.md](write-and-run-benchmarks.md).
