# Instruction: Orchestration Playbook

## Intent

Give the `orchestrator` a deterministic decision procedure for routing every non-trivial user request: either **parallel mode** (independent tasks, no ordering constraints) or **orchestration mode** (interdependent tasks with staged handoffs and a review gate). The playbook mirrors the parallel-vs-orchestration rules from [.github/agents/dotnet-self-learning-architect.agent.md](.github/agents/dotnet-self-learning-architect.agent.md) and operationalises them for the MediatorLite team of seven role agents.

## When to use

- The user issues any request more complex than a single-file edit.
- A role agent escalates a cross-role conflict to the orchestrator.
- A follow-up turn must pick up context from a prior agent's handoff.

## Agent ownership

- **Primary:** `orchestrator` — only agent authorised to make the mode decision and dispatch to role agents.
- **Consumed by:** every role agent (`scrum-master`, `backend-developer`, `frontend-developer`, `tester`, `devops`, `code-reviewer`) when reading their brief.

## Inputs / Preconditions

- A user request captured verbatim in the session (`sessions.user_request`, truncated to 2000 chars).
- The orchestrator has read [AGENTS.md](AGENTS.md) and [.github/copilot-instructions.md](.github/copilot-instructions.md) this session.
- The context DB is reachable (`.cursor/db/session.sqlite`) and `ContextDb.ReadRecent(agentName)` is available via the session helpers.

## Numbered steps

1. **Parse intent.** Classify the request into one of: `plan`, `backlog`, `implement`, `test`, `review`, `release`, `diagnose-consumer`, `meta`. Log the classification in `decisions` with `topic='intent'`.

2. **Apply the mode heuristic** from the self-learning architect policy:

   | Signal                                                  | Mode             |
   |---------------------------------------------------------|------------------|
   | Writes hit disjoint file sets                           | **Parallel**     |
   | Tasks are purely investigative (no shared artefact)     | **Parallel**     |
   | Same file is edited by two roles                        | **Orchestration**|
   | Tester needs backend's output to write the test         | **Orchestration**|
   | Review gate must run after code change                  | **Orchestration**|
   | Release tag must follow a green benchmarks + reviewer   | **Orchestration**|
   | Public API change proposed                              | **Orchestration** + human sign-off |

   Record the decision: `ContextDb.LogDecision("mode", "parallel"|"orchestration", rationale)`.

3. **The 7-agent team** (orchestrator + 6 role agents):

   ```mermaid
   flowchart TD
       U[User] --> O[orchestrator]
       O -->|plan / backlog| SM[scrum-master]
       O -->|implement| BE[backend-developer]
       O -->|ui / samples| FE[frontend-developer]
       O -->|tests / repro| T[tester]
       O -->|build / release / benchmarks| DO[devops]
       O -->|diff review gate| CR[code-reviewer]
       BE --> CR
       FE --> CR
       T --> CR
       CR --> O
       DO --> O
       SM --> O
       O --> U
   ```

4. **Dispatch — parallel mode.** Send one `Task` call per independent role agent, in the same orchestrator turn. Each brief must declare its own task boundary and forbid writes outside that boundary.

   Template brief (parallel):

   ```markdown
   You are the <slug> agent. Task: <one sentence>.
   Boundary: <paths, allowed writes>.
   Forbidden: <paths other parallel agents own this turn>.
   Upstream context: `ContextDb.ReadRecent(agentName: "orchestrator", limit: 5)`.
   Return the handoff contract block at the end of your turn.
   ```

5. **Dispatch — orchestration mode.** Send one `Task` at a time, in a fixed order. After each agent returns, the orchestrator:
   - Persists the handoff to `agent_messages` (`role='handoff'`).
   - Passes the last agent's output to the next via `ContextDb.ReadRecent(agentName: "<upstream>", limit: 10)` referenced in the next brief.
   - Enforces the **review gate**: any turn touching `src/**` or `tests/**` triggers `code-reviewer` against the staged `diff_hash` before continuing.

   Template brief (orchestration, downstream agent):

   ```markdown
   You are the <slug> agent. Task: <one sentence>.
   Upstream: read `ContextDb.ReadRecent(agentName: "<upstream-slug>", limit: 10)` —
   incorporate findings explicitly.
   Diff hash: <sha256 of `git diff --staged`>.
   Review gate: code-reviewer will be invoked after your turn with this diff_hash.
   Return the handoff contract block at the end of your turn.
   ```

6. **Consolidate.** Aggregate every agent's handoff block into a single user-facing reply. Dedupe `LessonsSuggested` / `MemoriesSuggested` by title; prefer the most specific `why`. Always include a combined `ReasoningSummary`.

7. **Self-learning finalisation.** For each unique suggestion, decide create / update / skip. Follow the Learning Governance rules (dedupe check, `PatternVersion` bump, `Supersedes` on conflict). See [adr-template.md](adr-template.md).

## Three concrete scenarios

### Scenario A: "Add feature X" (new request + handler)

- **Mode:** orchestration. Handler depends on the request record; test depends on the handler; reviewer runs last.
- **Sequence:**
  1. `scrum-master` → captures the acceptance criteria into `sprint_backlog`.
  2. `backend-developer` → executes [add-new-request-handler.md](add-new-request-handler.md). Stages diff.
  3. Orchestrator computes `diff_hash`, invokes `code-reviewer`.
  4. `tester` → writes or extends source-generation test, referencing backend's output via `ContextDb.ReadRecent(agentName: "backend-developer")`.
  5. `code-reviewer` → second pass on combined diff. Must land `No significant correctness findings.` or a resolved finding.
  6. Orchestrator consolidates and replies.

### Scenario B: "Fix bug in source gen" (defect in `HandlerDiscoveryGenerator`)

- **Mode:** orchestration. Test must exist before the fix; reviewer must gate the fix.
- **Sequence:**
  1. `tester` → writes the failing snapshot test per [bug-fix-workflow.md](bug-fix-workflow.md). Confirms red.
  2. Orchestrator passes the failing test name to `backend-developer` via the brief.
  3. `backend-developer` → edits [src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs](src/MediatorLite.SourceGeneration/HandlerDiscoveryGenerator.cs), staying inside the `IIncrementalGenerator` contract ([.cursor/rules/20-source-generator.mdc](.cursor/rules/20-source-generator.mdc)).
  4. `tester` → confirms green, adds regression guard.
  5. `code-reviewer` → inspects incrementality (no `INamedTypeSymbol` across cache boundaries) and dispatch-invariant compliance.
  6. Orchestrator writes `.github/Lessons/YYYY-MM-DD-<slug>.md`. Replies to user with lesson link.

### Scenario C: "Cut release 2.1.0"

- **Mode:** orchestration with explicit human sign-off (major/minor bumps).
- **Sequence:**
  1. Orchestrator asks the user to confirm the version bump (2.1.0 implies minor-level feature additions; human must confirm). Log a `decisions` row with `topic='public-api'`.
  2. `devops` → executes [release-workflow.md](release-workflow.md) steps 1–5 (bump `.csproj`s, update changelog, verify benchmarks, open release PR).
  3. `code-reviewer` → signs off on the release PR.
  4. `devops` → merges, creates `v2.1.0` tag, pushes. Publish workflow runs.
  5. `devops` → post-publish smoke test (`dotnet add package MediatorLite --version 2.1.0` in a scratch console app).
  6. Orchestrator consolidates, closes the session with `ContextDb.CloseSession(sid)`.

## Command cheat-sheet

- Compute diff hash:

  ```powershell
  (git diff --staged | Get-FileHash -Algorithm SHA256).Hash
  ```

- Build + full test loop that every mode must pass before close-out:

  ```powershell
  dotnet build MediatorLite.sln -c Release
  dotnet test  MediatorLite.sln -c Release --no-build
  ```

  Both must exit `0`.

## Validation / Acceptance

- A `decisions` row with `topic='mode'` exists for the current session.
- For orchestration mode: exactly one `code-reviewer` row in `reviews` per unique `diff_hash` before close-out.
- Every dispatched agent returned a handoff contract block (`LessonsSuggested` / `MemoriesSuggested` / `ReasoningSummary`); the orchestrator's final reply merges them.
- No role agent was spawned twice in the same turn during orchestration mode (that would break the serialised handoff log).

## Handoff / Exit criteria

- The user receives one consolidated reply ending with the literal handoff contract block.
- `sessions.status` is `closed` if the user's request is fully satisfied, `active` otherwise.
- All unique lesson/memory suggestions are either written, updated, or explicitly skipped-with-reason.

## Related rules, skills, instructions

- Architect policy: [.github/agents/dotnet-self-learning-architect.agent.md](.github/agents/dotnet-self-learning-architect.agent.md).
- Orchestrator agent: [.cursor/agents/orchestrator.md](.cursor/agents/orchestrator.md).
- Reviewer agent: [.github/agents/code-reviewer.agent.md](.github/agents/code-reviewer.agent.md).
- Rules: [.cursor/rules/00-project-conventions.mdc](.cursor/rules/00-project-conventions.mdc), [.cursor/rules/10-dispatch-invariants.mdc](.cursor/rules/10-dispatch-invariants.mdc), [.cursor/rules/20-source-generator.mdc](.cursor/rules/20-source-generator.mdc).
- Related instructions: [add-new-request-handler.md](add-new-request-handler.md), [bug-fix-workflow.md](bug-fix-workflow.md), [release-workflow.md](release-workflow.md), [adr-template.md](adr-template.md), [write-and-run-benchmarks.md](write-and-run-benchmarks.md).
