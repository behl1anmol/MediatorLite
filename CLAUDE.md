# MediatorLite — Claude Code Workspace

## Project

MediatorLite is a lightweight, source-generation-first mediator for .NET 10. It achieves
compile-time handler discovery, zero-reflection dispatch, and inlined logging/tracing. The
public surface is intentionally minimal: `IMediator`, `IRequest<T>`, `INotification`, and
a small set of attributes. See `AGENTS.md` and `README.md` for the full architecture picture.

---

## Agentic Team

This workspace has a **7-role scrum team** coordinated by an **orchestrator**. Route every
non-trivial request through the orchestrator — it selects the execution mode, dispatches to
role agents via the `Agent` tool, enforces the review gate, and consolidates replies.

See `.claude/agents.md` for the full team roster, routing rules, and escalation matrix.

| Agent | Slug | One-line role |
|-------|------|---------------|
| Orchestrator | `orchestrator` | Team lead; DB owner; mode selector; single entry point |
| Scrum Master | `scrum-master` | Read-only planner; DoR/DoD; standup digest |
| Backend Developer | `backend-developer` | `src/MediatorLite*/**` author |
| Frontend Developer | `frontend-developer` | `samples/**` and REST benchmark harness |
| Tester | `tester` | `tests/**` author; TDD enforcer |
| DevOps | `devops` | CI/CD, hooks, publish, benchmarks, DB schema |
| Code Reviewer | `code-reviewer` | Read-only diff reviewer; `reviews` table owner |

**When spawning a sub-agent** (via the `Agent` tool), load `.claude/agents/<slug>.md` and
pass its full content as the sub-agent's instructions. Include a pointer to recent
`agent_messages` so the sub-agent can rehydrate context.

---

## Session Initialisation

The hooks in `.claude/settings.json` handle session lifecycle automatically:

- **PreToolUse** → `01-bootstrap.csx` (creates SQLite session row once per session)
- **PreToolUse** → `05-inject-context.csx` (injects prior `agent_messages` at turn start)
- **PreToolUse on `git commit`** → `20-autoreview.csx`, `21-build-gate.csx`, `22-lint-gate.csx`
- **PreToolUse on `git push`** → `30-test-gate.csx`
- **PostToolUse (ExitPlanMode/TodoWrite)** → `40-snapshot-plan.csx`
- **PostToolUse (error)** → `10-log-mistake.csx`
- **Stop** → `00-save-context.csx` + `99-close-session.csx`

If hooks are not active (e.g. in a plain `dotnet-script` context), initialise manually:

```bash
dotnet script .claude/hooks/01-bootstrap.csx
```

The SQLite context DB lives at `.claude/db/session.sqlite` (gitignored).
Schema: `.claude/db/schema.sql`. Helper library: `.claude/lib/ContextDb.csx`.

Every role agent rehydrates cross-session context at turn start:

```csharp
#load ".claude/lib/ContextDb.csx"
using static ContextDb;
var msgs = ReadRecent(limit: 20);
```

---

## Always-Active Rules

The following rules apply to **every file and every agent turn**. They are non-negotiable.

@.claude/rules/00-project-conventions.md
@.claude/rules/10-dispatch-invariants.md
@.claude/rules/20-source-generator.md
@.claude/rules/30-pipeline-behaviors.md
@.claude/rules/40-notifications.md
@.claude/rules/50-validation.md
@.claude/rules/60-observability.md
@.claude/rules/70-testing.md
@.claude/rules/80-benchmarks.md
@.claude/rules/90-public-api-discipline.md

---

## Skills

Load these when relevant — agents are instructed to load them via the `Read` tool. Claude
Code registers them automatically as skills from `.claude/skills/*/SKILL.md`.

| Skill | When to load |
|-------|-------------|
| `agentic-workflow` | Any multi-agent dispatch; handoff contract; mode selection |
| `context-db-schema` | Any DB read/write; hook authoring |
| `mediatorlite-abstractions` | Public surface; attributes; `IMediator` |
| `mediatorlite-core` | Runtime dispatch; DI; `AddMediatorLite()` |
| `mediatorlite-source-generation` | Generator pipelines; `HandlerDiscoveryGenerator` |
| `mediatorlite-observability` | Logging/tracing; opt-out attributes |
| `mediatorlite-validation` | `IValidator<T>`; `DataAnnotationsValidator`; `ValidationBehavior` |
| `mediatorlite-tests` | Test layout; fixture patterns; `*Count` assertions |
| `mediatorlite-benchmarks` | BenchmarkDotNet setup; MediatR comparison |
| `mediatorlite-sample-sourcegen` | Canonical consumer wiring; `samples/**` |
| `mediatorlite-rest-api-benchmarks` | `ApiBenchmarkHost`; REST harness |

---

## Available Slash Commands

These commands are registered from `.claude/commands/` and are user-invocable:

| Command | Purpose |
|---------|---------|
| `/add-new-request-handler` | Add an `IRequest<T>` + handler pair |
| `/add-new-notification-handler` | Add a notification + N handlers |
| `/add-new-pipeline-behavior` | Add an open or closed pipeline behavior |
| `/add-new-validator` | Add a DataAnnotations or `IValidator<T>` validator |
| `/bug-fix-workflow` | TDD bug fix loop (failing test → fix → passing test → lesson) |
| `/extend-source-generator` | Add a new discovery pipeline to `HandlerDiscoveryGenerator` |
| `/orchestration-playbook` | Parallel vs orchestration decision procedure |
| `/release-workflow` | Bump version, merge, tag, publish to NuGet |
| `/write-and-run-benchmarks` | BenchmarkDotNet authoring and run workflow |
| `/adr-template` | Write a lightweight ADR under `.github/Memories/` |

---

## Hook Infrastructure — Cursor vs Claude Code Mapping

| Cursor event | Claude Code mapping | Hook script |
|---|---|---|
| `onSessionStart` | `PreToolUse` (bootstrap, idempotent) | `01-bootstrap.csx` |
| `beforeTurn` | `PreToolUse` (throttled ≤60 s) | `05-inject-context.csx` |
| `beforeCompaction` | `Stop` | `00-save-context.csx` |
| `onAgentError` | `PostToolUse` (`isError == true`) | `10-log-mistake.csx` |
| `afterPlanCreation` | `PostToolUse` (`ExitPlanMode` / `TodoWrite`) | `40-snapshot-plan.csx` |
| `beforeCommit` (chain) | `PreToolUse` (`git commit` pattern) | `20-autoreview.csx` → `21-build-gate.csx` → `22-lint-gate.csx` |
| `beforePush` | `PreToolUse` (`git push` pattern) | `30-test-gate.csx` |
| `onSessionEnd` | `Stop` | `99-close-session.csx` |

Hooks are implemented as `dotnet-script` (`.csx`) files invoked via bash or PowerShell shims.
The shims install `dotnet-script` globally on first run if missing.

### Environment overrides

| Variable | Effect |
|---|---|
| `MEDIATORLITE_SKIP_AUTOREVIEW=1` | Skip the code-review gate on commit |
| `MEDIATORLITE_SKIP_FORMAT=1` | Skip the lint/format gate on commit |
| `MEDIATORLITE_SKIP_TESTS=1` | Skip the test gate on push |
| `MEDIATORLITE_HOOK_VERBOSE=1` | Enable verbose hook tracing |

---

## Self-Learning Contract

Every successful agent turn **must** end with this literal block:

```
LessonsSuggested: <title>: <why>  OR  none
MemoriesSuggested: <title>: <why> OR  none
ReasoningSummary: <rationale>
```

The orchestrator consolidates these and decides whether to create or update files under
`.github/Lessons/` or `.github/Memories/` following the Learning Governance rules in
`.github/agents/dotnet-self-learning-architect.agent.md`.
