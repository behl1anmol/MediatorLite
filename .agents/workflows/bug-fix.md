# Workflow: Bug Fix (TDD-Ordered)

Fix a bug using test-driven development: write the failing test first, then fix.

## Steps

1. Ensure a session is active. If `.agents/session.env` is missing, Call `/start-session`.

2. **Tester phase (red):**
   - Write a failing test in `tests/MediatorLite.Tests/` that reproduces the bug.
   - Run: `dotnet test MediatorLite.sln --no-build 2>&1 | tail -30`
   - Confirm the new test is red (failing). If it passes already, the bug is already fixed.
   - Log decision: `dotnet-script .agents/scripts/log-decision.csx "bug-repro" "test-written" "Failing test confirms bug" "tester"`

3. **Backend phase (fix):**
   - Identify the root cause in `src/MediatorLite*/**`.
   - Apply the minimal fix. Do not change public API without architecture decision (see rule 90).
   - Run: `dotnet build MediatorLite.sln -c Release --nologo -v q`
   - Confirm build green.

4. **Tester phase (green):**
   - Run: `dotnet test MediatorLite.sln -c Release --no-build`
   - Confirm all tests pass including the new one.

5. **Review gate:**
   - Call `/code-review` with the current diff.

6. **Log outcome:**
   Run: `dotnet-script .agents/scripts/log-decision.csx "bug-fix" "complete" "All tests green, review gate passed" "backend-developer"`

7. Emit handoff block (LessonsSuggested / MemoriesSuggested / ReasoningSummary).
