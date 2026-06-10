# Workflow: Code Review

Runs the code-reviewer agent against the current uncommitted diff.

## Steps

1. Get the current diff:
   Run: `git diff HEAD`
   If empty, report that there are no changes to review.

2. Act as the code-reviewer agent. Analyze the diff for:
   - Proper use of the generated `SourceGeneratedMediator` dispatch and MediatorLite DI rules.
   - Any public API surface additions without architecture decisions.
   - Missing tests or edge cases not covered.

3. Log any findings to the session DB:
   Run: `dotnet-script .agents/scripts/log-review.csx "<target_file>" "<severity: Critical/High/Medium/Low>" "<finding>" "<optional_suggested_fix>"`
   *Make one call for each significant finding.*

4. Present the review results to the user.
