# Instruction: Bug Fix Workflow (TDD)

## Intent

Fix a reported bug using a strict test-driven loop: **reproduce → failing test → fix → passing test → regression guard → lesson**. Every bug becomes a permanent regression test plus a durable lesson under `.github/Lessons/` so the same failure cannot slip back in silently.

## When to use

- A user-reported defect, a CI failure, or an unexpected test flake.
- A review finding from `code-reviewer` rated `High` or `Critical`.
- A regression discovered by the benchmark workflow (e.g. allocation jump, throughput drop).

## Agent ownership

- **Primary (writes the failing test first):** `tester`.
- **Primary (writes the fix):** `backend-developer`.
- **Review gate:** `code-reviewer` — must sign off on both the test and the fix.
- **Orchestrator:** coordinates the sequential handoff and enforces the review gate.

## Inputs / Preconditions

- A clear bug description (user-visible symptom) and ideally a minimal reproduction.
- Current `main` (or target branch) builds and tests green: `dotnet test MediatorLite.sln -c Release` exits `0`.
- A dedicated branch: `fix/<short-slug>`.

## Numbered steps

1. **Reproduce manually** before writing anything. Either in `samples/MediatorLite.Sample.SourceGen` or via a `dotnet script`-style scratch test. Confirm the observed vs. expected behaviour in writing.

2. **`tester`: write a failing test first.** Place it in the appropriate file under [tests/MediatorLite.Tests/](tests/MediatorLite.Tests/):
   - Source-generation behaviour → [tests/MediatorLite.Tests/SourceGeneration/](tests/MediatorLite.Tests/SourceGeneration/).
   - Pure unit → `tests/MediatorLite.Tests/UnitTests/`.
   The test must fail for the **right reason** — assert the specific observable that the bug violates, not an incidental side-effect.

   Run only the new test to confirm it fails:

   ```powershell
   dotnet test MediatorLite.sln -c Release --filter FullyQualifiedName~<NewTestName>
   ```

   Expected: exit code non-zero, assertion failure quoted in the output.

3. **`backend-developer`: fix.** Keep the fix minimal and local:
   - Edit the narrowest file that contains the defect.
   - Preserve the invariants in [.claude/rules/10-dispatch-invariants.mdc](.claude/rules/10-dispatch-invariants.mdc). Specifically, do not reintroduce reflection in [Mediator.cs](src/MediatorLite/Internal/Mediator.cs):

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

   - Do not alter the public API of `AddMediatorLite()` or introduce new overloads (see [.claude/rules/00-project-conventions.mdc](.claude/rules/00-project-conventions.mdc)).

4. **Confirm the failing test now passes and nothing else broke**:

   ```powershell
   dotnet build MediatorLite.sln -c Release
   dotnet test  MediatorLite.sln -c Release --no-build
   ```

   Both must exit `0`. Targeted rerun:

   ```powershell
   dotnet test MediatorLite.sln -c Release --no-build --filter FullyQualifiedName~<NewTestName>
   ```

5. **Add a regression guard.** If the bug was caused by a missing code path, ensure the new test **also** asserts the adjacent, still-working paths. If the bug was a subtle ordering/short-circuit issue, add a second test that pins the ordering. For notification strategy bugs, write the three-test parity block described in [add-new-notification-handler.md](add-new-notification-handler.md).

6. **Write a lesson** under `.github/Lessons/YYYY-MM-DD-<slug>.md` using the template from [.github/agents/dotnet-self-learning-architect.agent.md](.github/agents/dotnet-self-learning-architect.agent.md):

   ```markdown
   # Lesson: <short-title>

   ## Metadata
   - PatternId:
   - PatternVersion: 1
   - Status: active
   - Supersedes:
   - CreatedAt: YYYY-MM-DD
   - LastValidatedAt: YYYY-MM-DD
   - ValidationEvidence: PR #NNN, commit <sha>, test <FullyQualifiedName>

   ## Task Context
   - Triggering task: <bug ID or user report>
   - Date/time: YYYY-MM-DD
   - Impacted area: src/MediatorLite/...

   ## Mistake
   - What went wrong:
   - Expected behavior:
   - Actual behavior:

   ## Root Cause Analysis
   - Primary cause:
   - Contributing factors:
   - Detection gap:

   ## Resolution
   - Fix implemented:
   - Why this fix works:
   - Verification performed: <test names, benchmark results>

   ## Preventive Actions
   - Guardrails added: <new test(s), new analyzer, new doc note>
   - Process updates:

   ## Reuse Guidance
   - How to apply this lesson in future tasks:
   ```

   Run the anti-repetition check described in the architect guide before creating the file: if a similar `active` lesson exists, update it and bump `PatternVersion` instead of creating a duplicate.

7. **Code-review gate.** `code-reviewer` inspects:
   - The failing-then-passing test is asserting the **cause**, not a symptom.
   - The fix is minimal and respects the dispatch invariants.
   - Warnings-as-errors is still clean (`dotnet build -c Release` exit `0`).
   - A lesson file was created or an existing one updated.

8. **Merge.** Once the reviewer posts `No significant correctness findings.` or a resolved finding, hand back to `devops` for merge. Do **not** `--force` push and do **not** amend after review.

## Validation / Acceptance

- `dotnet build MediatorLite.sln -c Release` — exit code `0`, no new warnings.
- `dotnet test MediatorLite.sln -c Release --no-build` — exit code `0`. The new test was red before the fix and green after.
- A `.github/Lessons/YYYY-MM-DD-<slug>.md` file exists (or an existing lesson was updated with `LastValidatedAt` bumped and a new `ValidationEvidence` entry).
- No new public API surface was added to fix a bug (if it was, the fix is actually a feature — reroute through [orchestration-playbook.md](orchestration-playbook.md)).

## Handoff / Exit criteria

- `orchestrator` confirms the `reviews` row keyed on `diff_hash` exists with no unresolved `High`/`Critical` findings.
- Final PR description links to the lesson file and the new test's fully-qualified name.
- Close the source session with `ContextDb.CloseSession(sid)` once merged.

## Related rules, skills, instructions

- Rules: [.claude/rules/00-project-conventions.mdc](.claude/rules/00-project-conventions.mdc), [.claude/rules/10-dispatch-invariants.mdc](.claude/rules/10-dispatch-invariants.mdc).
- Self-learning contract: [.github/agents/dotnet-self-learning-architect.agent.md](.github/agents/dotnet-self-learning-architect.agent.md).
- Core files the fix often touches: [src/MediatorLite/Internal/Mediator.cs](src/MediatorLite/Internal/Mediator.cs), [src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs).
- Tests: [tests/MediatorLite.Tests/SourceGeneration/](tests/MediatorLite.Tests/SourceGeneration/).
- Agents: [.claude/agents/orchestrator.md](.claude/agents/orchestrator.md), [.github/agents/code-reviewer.agent.md](.github/agents/code-reviewer.agent.md).
- Related instructions: [adr-template.md](adr-template.md), [orchestration-playbook.md](orchestration-playbook.md).
