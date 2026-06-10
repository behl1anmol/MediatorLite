---
name: agentic-workflow
description: How every role agent participates in the MediatorLite scrum team. Covers the 7 roles (orchestrator, scrum-master, backend-developer, frontend-developer, tester, devops, code-reviewer), the parallel-vs-orchestration decision matrix, the required handoff output contract (`LessonsSuggested` / `MemoriesSuggested` / `ReasoningSummary`), the lesson and memory file templates, the Learning Governance rules (PatternId / PatternVersion / Status / Supersedes / pre-write dedupe / conflict resolution / safety gate / reuse priority), and how each agent calls `.claude/lib/ContextDb.csx` to log decisions, handoffs, mistakes, reviews, and backlog items. Use whenever a role agent is spawned or asked to cooperate with another role.
triggers: scrum team, agentic workflow, orchestrator, scrum-master, backend-developer, frontend-developer, tester, devops, code-reviewer, handoff, LessonsSuggested, MemoriesSuggested, ReasoningSummary, lesson template, memory template, pattern governance, PatternId, PatternVersion, Supersedes, multi-agent, parallel mode, orchestration mode, dotnet-self-learning-architect
---

# Agentic Workflow

## Purpose

MediatorLite is built by a simulated scrum team of specialist role agents, not by a single monolithic assistant. This skill tells any role agent — or the orchestrator dispatching them — exactly how to:

1. Pick the right delegation mode (parallel vs orchestration).
2. Hand off to another role with the required output contract.
3. Capture durable context via the lesson & memory templates **and** the SQLite session DB.
4. Respect Learning Governance so the knowledge base does not drift into contradictions.

Read this before spawning a subagent or before returning from your own Task tool invocation.

## When to use

- You are the orchestrator and deciding between parallel or orchestration mode for an incoming request.
- You are a role agent (e.g. `backend-developer`) completing a task and about to return to the orchestrator.
- A subagent's completion message is missing the handoff block — this skill is the contract to enforce.
- A lesson or memory needs to be created, updated, or deprecated.
- The team is onboarding a brand-new role; align its behavior with the canonical contract here.

## The 7 roles (quick reference)

Each role has (or will have) a dedicated agent file under `.github/agents/*.agent.md`. This skill summarises; the agent files are authoritative for role-specific rules.

| Role | Primary responsibility | Authoritative agent file |
|------|------------------------|--------------------------|
| **orchestrator** | Own the session, break the request into work, choose delegation mode, consolidate outputs, maintain lessons/memories. Plays the "architect" seat. | [.github/agents/dotnet-self-learning-architect.agent.md](.github/agents/dotnet-self-learning-architect.agent.md) |
| **scrum-master** | Maintain the `sprint_backlog`, prioritise, unblock, confirm Definition of Done before closing items. | (dedicated file pending) |
| **backend-developer** | Implement changes in `src/MediatorLite/**`, `src/MediatorLite.Abstractions/**`, `src/MediatorLite.SourceGeneration/**`. Must keep the generated `SourceGeneratedMediator` (implements `IMediator`) aligned with the abstractions. | (dedicated file pending) |
| **frontend-developer** | Sample apps and any non-library UI/console surface (e.g. `samples/MediatorLite.Sample.SourceGen`). | (dedicated file pending) |
| **tester** | xUnit + FluentAssertions coverage under `tests/MediatorLite.Tests`, benchmarks under `tests/MediatorLite.Benchmarks` and `tests/MediatorLite.RestApiBenchmarks`. Owns `dotnet test MediatorLite.sln`. | (dedicated file pending) |
| **devops** | Build, CI, NuGet packaging, `Directory.Build.props`, `AGENTS.md` / `.github/copilot-instructions.md` sync. | (dedicated file pending) |
| **code-reviewer** | Read-only correctness/security/perf review of diffs; emits findings into the `reviews` table. | [.github/agents/code-reviewer.agent.md](.github/agents/code-reviewer.agent.md) |

The orchestrator never writes production code directly when a specialist role exists. The code-reviewer never edits code.

## Delegation mode decision matrix

Borrowed from the self-learning architect's contract. Choose **explicitly** before delegating. If the boundary is unclear, ask the user.

| Signal | Parallel Mode | Orchestration Mode |
|--------|---------------|--------------------|
| Shared write target | None | Yes (same files / component) |
| Ordering constraint | None | Staged handoffs (dev → review → test) |
| Cross-role sign-off | Not required | Required (e.g. reviewer must approve before commit) |
| Architectural/security risk | Low | High |
| Conflict risk on outputs | Low | Medium-High |
| Example | Independent doc draft + separate test-impact analysis | Implement feature → review → test → devops |

Rules:

- Default to parallel when tasks are mutually independent and cheap to consolidate.
- Switch to orchestration the moment two subagents would write to overlapping files or one depends on the other's output.
- When in doubt, confirm the mode with the user and present "why orchestration > parallel" in one sentence.
- Parent/orchestrator synthesises outputs in either mode — subagents never commit results directly.

## Handoff output contract (REQUIRED for every subagent)

Every Task-tool-invoked role agent **must** terminate its final message with this block exactly:

```
LessonsSuggested:
- <title>: <why this lesson is suggested>
- <title-2>: <optional>
(or)
- none

MemoriesSuggested:
- <title>: <why this memory is suggested>
(or)
- none

ReasoningSummary:
- <1–3 bullets of concise rationale, trade-offs, confidence>
```

Rules (mirroring `.github/agents/dotnet-self-learning-architect.agent.md`):

- If nothing new is worth persisting, return `- none` explicitly — don't omit the section.
- `ReasoningSummary` is **always** required after successful completion (it's not optional).
- Keep each bullet evidence-based and tied to the completed task. No filler.
- The parent agent consolidates, deduplicates, and *then* materialises files under `.github/Lessons/` / `.github/Memories/`. Subagents suggest; they do not create.

Complementary, every agent should also log its handoff into `agent_messages` via `ContextDb.WriteMessage(...)` so the next turn can rehydrate it. See [.claude/skills/context-db-schema/SKILL.md](.claude/skills/context-db-schema/SKILL.md).

## Lesson template

Create at `.github/Lessons/YYYY-MM-DD-<kebab-title>.md`. Summarised from [dotnet-self-learning-architect.agent.md lines 162–201](.github/agents/dotnet-self-learning-architect.agent.md):

```markdown
# Lesson: <short-title>

## Metadata
- PatternId:            <stable slug, e.g. behavior-registration-multi-interface>
- PatternVersion:       1
- Status:               active   # active | deprecated | blocked
- Supersedes:           <PatternId of older pattern, or none>
- CreatedAt:            2026-MM-DD
- LastValidatedAt:      2026-MM-DD
- ValidationEvidence:   <tests, benchmarks, PR link>

## Task Context
- Triggering task:
- Date/time:
- Impacted area:        <paths / components>

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
- Verification performed:

## Preventive Actions
- Guardrails added:
- Tests/checks added:
- Process updates:

## Reuse Guidance
- How to apply this lesson in future tasks:
```

Reference example: [.github/Lessons/2026-03-19-servicecollectionextensions-options-validation-review.md](.github/Lessons/2026-03-19-servicecollectionextensions-options-validation-review.md).

When creating a lesson, also log the mistake row:

```csx
#load ".claude/lib/ContextDb.csx"
ContextDb.LogMistake(
    agent:      "backend-developer",
    category:   "review",
    summary:    "FirstOrDefault silently drops multi-interface behavior registrations",
    rootCause:  "Selection projected to a single interface",
    fix:        "Introduced PipelineBehaviorTypeResolver",
    lessonFile: ".github/Lessons/2026-03-19-servicecollectionextensions-options-validation-review.md");
```

## Memory template

Create at `.github/Memories/<kebab-title>.md`. Summarised from the same architect file:

```markdown
# Memory: <short-title>

## Metadata
- PatternId:            <stable slug>
- PatternVersion:       1
- Status:               active   # active | deprecated | blocked
- Supersedes:           none
- CreatedAt:            2026-MM-DD
- LastValidatedAt:      2026-MM-DD
- ValidationEvidence:   <PR/issue/bench link>

## Source Context
- Triggering task:
- Scope/system:
- Date/time:

## Memory
- Key fact or decision:
- Why it matters:

## Applicability
- When to reuse:
- Preconditions/limitations:

## Actionable Guidance
- Recommended future action:
- Related files/services/components:
```

Reference example: [.github/Memories/servicecollectionextensions-options-validation-risks.md](.github/Memories/servicecollectionextensions-options-validation-risks.md).

## Learning Governance (MUST be followed)

The governance rules live in [.github/agents/dotnet-self-learning-architect.agent.md](.github/agents/dotnet-self-learning-architect.agent.md); this is the compressed reference you must internalise:

### 1. Versioned patterns (required)

Every lesson and memory carries `PatternId`, `PatternVersion`, `Status`, and `Supersedes`. Increment `PatternVersion` for any meaningful guidance change. `Status` is one of `active`, `deprecated`, `blocked`.

### 2. Pre-write dedupe (required)

**Before** writing a new lesson/memory, search `.github/Lessons` and `.github/Memories` for similar:

- root cause,
- decision,
- impacted area,
- applicability.

If a close match exists, **update** that record (bump `PatternVersion`, refresh `LastValidatedAt`, add evidence). Only create a new file when the pattern is materially distinct.

### 3. Conflict resolution (required)

If new evidence conflicts with an existing `active` pattern:

1. Do **not** keep both as active.
2. Mark the older one `deprecated` (or `blocked` if unsafe to reuse).
3. Create/update the replacement with `Supersedes: <old-PatternId>`.
4. **Tell the user**: what changed, why, and which pattern supersedes which.

### 4. Safety gate (required)

Never apply or recommend any pattern whose `Status: blocked`. Reactivation requires explicit validation evidence **and** explicit user confirmation.

### 5. Reuse priority (required)

Prefer the newest validated `active` pattern. If confidence is low or an unresolved conflict remains, ask the user before applying the guidance.

## Using `.claude/lib/ContextDb.csx` in agent flow

Every role writes to the session DB at well-defined moments. Full helper reference lives in [.claude/skills/context-db-schema/SKILL.md](.claude/skills/context-db-schema/SKILL.md).

Minimum contract per role event:

| Event | Helper call |
|-------|-------------|
| Orchestrator begins session | `ContextDb.EnsureSession(userRequest: "...", branch: "...")` |
| Any agent makes a non-trivial decision | `ContextDb.LogDecision(topic, choice, rationale, agent)` |
| Agent hands off to another role | `ContextDb.WriteMessage(agent, "handoff", content, target: "<next-role>")` |
| Agent reports a finding back | `ContextDb.WriteMessage(agent, "finding", content)` |
| Mistake is detected (build break, failing test, review fix) | `ContextDb.LogMistake(agent, category, summary, rootCause, fix, lessonFile)` |
| Code-reviewer produces findings | `ContextDb.LogReview(target, severity, finding, suggestedFix, diffHash)` |
| Autoreview gate checks an unchanged diff | `ContextDb.HasFreshReview(diffHash, TimeSpan.FromHours(2))` |
| Plan-mode artefact created | `ContextDb.SnapshotPlan(title, path, body, createdBy, status)` |
| Scrum-master registers a backlog item | `ContextDb.AddBacklogItem(story, acceptance, assignedAgent, priority)` |
| Hook completes | `ContextDb.LogHookEvent(hookName, eventType, outcome, durationMs, payload)` |
| Session ends | `ContextDb.CloseSession(id, status: "closed")` + optional `Vacuum()` |

### Example: backend-developer handoff to tester

```csx
#load ".claude/lib/ContextDb.csx"
var story = """
Refactored ValidationBehavior to short-circuit when Validators.Count == 0 (no change).
Added a fast-path log when the behavior is skipped.
Please cover:
  - no-validator request type (no log emitted)
  - one-validator request type (skip path log)
  - two-validator request type with both failing (errors concatenated in order)
""";
ContextDb.WriteMessage("backend-developer", "handoff", story, target: "tester");
ContextDb.LogDecision(
    topic:     "validation-fast-path",
    choice:    "Log at Debug when Validators.Count == 0",
    rationale: "Makes 'is validation wired?' diagnosable without rebuilding.",
    agent:     "backend-developer");
```

### Example: code-reviewer gating a commit

```csx
#load ".claude/lib/ContextDb.csx"
var diffHash = /* sha256 of `git diff --staged` */ "sha256-...";
if (!ContextDb.HasFreshReview(diffHash, TimeSpan.FromHours(2)))
{
    // spawn code-reviewer subagent, then log its findings:
    ContextDb.LogReview(
        target:       "src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs",
        severity:     "High",
        finding:      "SendAsync drops cancellation on sync-completed handlers",
        suggestedFix: "Observe token before fast-path return",
        diffHash:     diffHash);
}
```

## Common tasks

1. **Starting a feature.** Orchestrator calls `EnsureSession`, snapshots the plan with `SnapshotPlan`, chooses mode, spawns the right role(s).
2. **Decomposing a story.** Scrum-master calls `AddBacklogItem` for each acceptance criterion.
3. **Returning from a Task invocation.** Emit the mandatory handoff block **and** write an `agent_messages` row so the next turn can see it.
4. **Proposing a lesson.** Subagent suggests in `LessonsSuggested`. Parent runs the dedupe check, either updates the existing pattern or creates a new file, then calls `LogMistake` referencing the file path.
5. **Gating commit on a review.** Hash the diff; call `HasFreshReview`; if stale, spawn code-reviewer; then `LogReview` per finding before releasing the commit gate.
6. **Deprecating a superseded pattern.** Flip `Status: deprecated` on the old file, set `Supersedes` on the new, tell the user in plain text.

## Pitfalls

- **Skipping the mandatory handoff block.** A subagent that returns without `LessonsSuggested` / `MemoriesSuggested` / `ReasoningSummary` violates the contract. Do not accept the result; re-prompt.
- **Creating duplicate lessons.** A new file for a pattern already covered by an existing one. Always grep `.github/Lessons` first; prefer updating and bumping `PatternVersion`.
- **Leaving `Supersedes` empty when the new pattern contradicts an older active one.** The older one silently stays active and agents may pick the wrong guidance. Always mark the older `deprecated` and wire `Supersedes`.
- **Running subagents in parallel when they write the same file.** Classic race — expect last-writer-wins merge damage. Switch to orchestration mode.
- **Calling user-facing hotfixes without logging a decision.** The decision is lost between sessions. Always `LogDecision` for non-trivial choices.
- **Applying `Status: blocked` guidance.** Safety gate: never apply unless explicitly re-validated + user-confirmed.
- **Assuming `MEDIATORLITE_SESSION_ID` propagates into subagents.** Propagate it explicitly via the subagent brief (the Task tool's subagent environment is not guaranteed to inherit all parent vars).
- **Skipping `ReasoningSummary` on successful completion.** It is always required; "successful" is not a reason to omit it.
- **Confusing roles.** The code-reviewer is read-only. Do not ask it to edit code — spawn a developer role for that.
- **Committing code without an autoreview gate.** Every commit should either carry a fresh `reviews` row for its diff hash, or a logged decision to skip review with rationale.

## Related

- [.github/agents/dotnet-self-learning-architect.agent.md](.github/agents/dotnet-self-learning-architect.agent.md) — authoritative orchestrator playbook, full Learning Governance text.
- [.github/agents/code-reviewer.agent.md](.github/agents/code-reviewer.agent.md) — code-reviewer contract and output shape.
- [.claude/skills/context-db-schema/SKILL.md](.claude/skills/context-db-schema/SKILL.md) — full `ContextDb.csx` helper reference.
- [.github/Lessons/2026-03-19-servicecollectionextensions-options-validation-review.md](.github/Lessons/2026-03-19-servicecollectionextensions-options-validation-review.md) — canonical lesson example.
- [.github/Memories/servicecollectionextensions-options-validation-risks.md](.github/Memories/servicecollectionextensions-options-validation-risks.md) — canonical memory example.
- [AGENTS.md](AGENTS.md) — repo-level agent guide.
