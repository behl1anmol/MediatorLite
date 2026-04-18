# Instruction: Lightweight ADR Template

## Intent

Capture architecture decisions as short, queryable markdown memories under `.github/Memories/` with versioned pattern metadata. Decisions use the same template skeleton as the `.NET Self-Learning Architect` memory contract, so the anti-repetition governance rules (`PatternId`, `PatternVersion`, `Status`, `Supersedes`) apply uniformly to both ADRs and durable memories.

## When to use

- Choosing between two or more viable architectural options (e.g. sequential vs. parallel notifications at assembly scope, OpenTelemetry tag schema, whether to expose a new attribute).
- Deciding to keep or remove a deprecated public API (e.g. `MediatorGenerationAttribute`, which is retained as `[Obsolete]`).
- Recording a rejected option plus the reason, so future agents don't re-propose it.

## Agent ownership

- **Primary author:** any role agent that makes the decision in the course of its work.
- **Consolidator:** `orchestrator` — dedupes, checks `Supersedes` links, merges conflicting decisions per the Learning Governance rules.
- **Reviewer:** `code-reviewer` is pulled in only for decisions that affect the public API surface.

## Inputs / Preconditions

- You have read the self-learning architect governance rules in [.github/agents/dotnet-self-learning-architect.agent.md](.github/agents/dotnet-self-learning-architect.agent.md), specifically "Learning Governance (Anti-Repetition and Drift Control)".
- You have performed a pre-write dedupe check: searched `.github/Memories/` for similar root cause, decision, or impacted area. If a close match exists, **update that record** and bump `PatternVersion` rather than creating a new file.

## Numbered steps

1. **Pick a slug.** Use `kebab-case` that names the decision, not the outcome. Good: `notification-strategy-resolution-order`. Bad: `use-sequential-by-default`.

2. **Create the file** at `.github/Memories/<slug>.md` with front-matter metadata identical to the self-learning architect template:

   ```markdown
   # Memory: <short-title>

   ## Metadata
   - PatternId:            <stable ID, e.g. ADR-0007>
   - PatternVersion:       1
   - Status:               active | deprecated | blocked
   - Supersedes:           <slug or PatternId of the record this replaces, or empty>
   - CreatedAt:            YYYY-MM-DD
   - LastValidatedAt:      YYYY-MM-DD
   - ValidationEvidence:   <PR link, commit sha, test or benchmark FQN>

   ## Source Context
   - Triggering task:      <user request, bug ID, or issue link>
   - Scope/system:         <file paths, subsystem>
   - Date/time:            YYYY-MM-DD

   ## Memory
   - Key fact or decision: <1-2 sentences>
   - Why it matters:       <consequence, risk mitigated, perf claim>

   ## Applicability
   - When to reuse:        <triggers, shapes of problem>
   - Preconditions/limitations: <where this decision does NOT apply>

   ## Actionable Guidance
   - Recommended future action: <the default move for agents>
   - Related files/services/components:
   ```

3. **Fill in the ADR-specific sections.** Add these four sections below `Actionable Guidance` to preserve the ADR shape users expect:

   ```markdown
   ## Context

   <What forces this decision? What pressure existed in the codebase right before? Link
   to the concrete file / rule / issue.>

   ## Options considered

   - **Option A — <name>.** Pros: ... Cons: ...
   - **Option B — <name>.** Pros: ... Cons: ...
   - **Option C — <name>.** Rejected because: ...

   ## Decision

   <One-paragraph statement: "We will use Option B because ...". Be specific about the
   code-level consequence — which file changes, which method is added or removed.>

   ## Consequences

   - Positive: <allocation win, API simplicity, testability>.
   - Negative: <new constraint, learning curve, technical debt incurred>.
   - Neutral: <rename, dependency bump>.

   ## Revisit-in

   <Date or milestone at which this decision should be re-evaluated — e.g. "v2.0" or
   "2027-01" or "when NotificationErrorStrategy gains a third member".>
   ```

4. **Apply the Learning Governance rules** before committing:
   - **Dedupe check:** search `.github/Memories/` for overlapping `Scope/system` or `Key fact or decision`. If found, update the existing file and set `Supersedes:` there, or set `Supersedes:` here and mark the older file `Status: deprecated`.
   - **Conflict resolution:** if two active records reach opposite decisions, mark the older one `deprecated` (or `blocked` if unsafe) and call the change out in the consolidated reply to the user.
   - **Safety gate:** never write `Status: active` for guidance that contradicts an existing `blocked` pattern without explicit user confirmation.

5. **Commit the ADR in the same PR as the change it describes** when possible. This gives reviewers the "why" alongside the "what". If the ADR is written retroactively (e.g. for something already on `main`), cite the merge commit in `ValidationEvidence`.

6. **Verify the file is reachable and parseable**:

   ```powershell
   Get-Content .github\Memories\<slug>.md | Select-Object -First 20
   ```

   The first 20 lines should include the `# Memory:` heading and the full `## Metadata` block. All metadata keys must be present (blank values are acceptable but the keys must exist).

## Validation / Acceptance

- File exists at `.github/Memories/<slug>.md` with all required metadata keys.
- `PatternVersion` is `1` for a brand-new record, or incremented from the previous version if updating.
- `Supersedes` is empty, or points at a real file/PatternId in `.github/Memories/` or `.github/Lessons/`.
- Options considered list contains at least one rejected option with rationale (prevents future re-proposal).
- `Revisit-in` is a date, a milestone, or a specific event — never "never".

## Handoff / Exit criteria

- Orchestrator consolidates the new ADR into the session's reply and, if this ADR deprecates a previous decision, explicitly tells the user what changed and why.
- If the ADR changes a public API default, `orchestrator` requires human sign-off before the change lands in code ([.cursor/agents/orchestrator.md](.cursor/agents/orchestrator.md)).

## Related rules, skills, instructions

- Self-learning contract: [.github/agents/dotnet-self-learning-architect.agent.md](.github/agents/dotnet-self-learning-architect.agent.md).
- Existing memories/lessons root: `.github/Lessons/`, `.github/Memories/`.
- Orchestrator workflow: [.cursor/agents/orchestrator.md](.cursor/agents/orchestrator.md).
- Repo conventions: [.cursor/rules/00-project-conventions.mdc](.cursor/rules/00-project-conventions.mdc), [AGENTS.md](AGENTS.md).
- Related instructions: [bug-fix-workflow.md](bug-fix-workflow.md), [orchestration-playbook.md](orchestration-playbook.md).
