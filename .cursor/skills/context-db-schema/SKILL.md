---
name: context-db-schema
description: Read and write `.claude/db/session.sqlite` correctly via `.claude/lib/ContextDb.csx`. Walks every table in the schema (sessions, agent_messages, plans, decisions, mistakes, reviews, sprint_backlog, hook_events), documents every helper method with a runnable dotnet-script example, explains the `MEDIATORLITE_SESSION_ID` env var contract, the WAL journal mode + `foreign_keys = ON` pragmas, and shows how a Task-tool-spawned subagent can issue a one-shot `.csx` query. Use when a hook, subagent, or tool needs to persist cross-chat context or audit prior sessions.
triggers: SQLite context, session database, ContextDb, agent memory, cross-chat persistence, agent_messages, log decision, log mistake, log review, snapshot plan, sprint backlog, hook_events, MEDIATORLITE_SESSION_ID, .claude/db/session.sqlite, ContextDb.csx, dotnet-script, schema.sql
---

# Context DB Schema & `ContextDb.csx`

## Purpose

Every MediatorLite agent, hook, and subagent that needs durable cross-chat memory reads and writes a single SQLite database at `.claude/db/session.sqlite`. The schema lives in [.claude/db/schema.sql](.claude/db/schema.sql) and is applied idempotently by [.claude/lib/ContextDb.csx](.claude/lib/ContextDb.csx) on first use. This skill is the canonical reference for both.

**Do not hand-roll Microsoft.Data.Sqlite connection strings, CREATE TABLE statements, or ad-hoc ISO-8601 formatting in hook scripts.** Always use the helpers in `ContextDb.csx`.

## When to use

- Writing a new `.csx` hook that must persist or read session state.
- Writing a subagent brief that instructs the subagent to record a decision, mistake, handoff, or review finding.
- Auditing which decisions/plans/reviews were logged in this or a prior session.
- Extending the schema (add a new table or column) — you must update **both** `schema.sql` and a helper method in `ContextDb.csx` and communicate the migration plan.
- Debugging a hook that reports `SQLite Error 19: 'FOREIGN KEY constraint failed'` — usually a session ID problem.

## Entry points

- Schema: [.claude/db/schema.sql](.claude/db/schema.sql)
- Helpers: [.claude/lib/ContextDb.csx](.claude/lib/ContextDb.csx)
- Database file (created on first run): `.claude/db/session.sqlite` (+ WAL/SHM siblings).
- Every `.csx` hook starts with:
  ```csharp
  #load "../lib/ContextDb.csx"
  using static ContextDb;
  ```

## Design invariants

- **WAL journal mode.** Enabled at every connection via `PRAGMA journal_mode = WAL;` so concurrent readers (e.g. a subagent inspecting history) never block the writer.
- **Foreign keys are ON.** Enforced via `PRAGMA foreign_keys = ON;` on every connection. Every `session_id` column is a real FK; inserting into `agent_messages` / `plans` / `decisions` / `mistakes` / `reviews` / `sprint_backlog` with an unknown session ID raises SQLite error 19.
- **Session handle is the `MEDIATORLITE_SESSION_ID` environment variable.** `EnsureSession()` reads it first and returns it if the row exists; otherwise it generates a new UUID-v4 (N-format), inserts a `sessions` row with status `active`, and sets the env var so subsequent calls in the same process reuse it.
- **All timestamps are ISO-8601 UTC strings** produced via `DateTime.UtcNow.ToString("o")`. SQLite has no native `DateTime`; do not insert binary or integer ticks.
- **Keys are `INTEGER PRIMARY KEY AUTOINCREMENT`** except for `sessions.id` which is a UUID `TEXT`.
- **Schema is idempotent.** `schema.sql` is `CREATE TABLE IF NOT EXISTS` + `CREATE INDEX IF NOT EXISTS` only. Re-running it is safe.
- **No destructive migrations by helpers.** `Vacuum()` is the only maintenance operation exposed. Any real migration must be scripted explicitly and reviewed.

## Schema walkthrough

Full source: [.claude/db/schema.sql](.claude/db/schema.sql).

### `sessions`

```18:29:.claude/db/schema.sql
CREATE TABLE IF NOT EXISTS sessions (
    id            TEXT    PRIMARY KEY,           -- UUID v4, also exported as MEDIATORLITE_SESSION_ID
    started_at    TEXT    NOT NULL,              -- ISO-8601 UTC
    ended_at      TEXT    NULL,
    user_request  TEXT    NULL,                  -- first user message, truncated to 2000 chars
    status        TEXT    NOT NULL DEFAULT 'active',   -- active | compacted | closed | errored
    workspace     TEXT    NOT NULL DEFAULT '',   -- absolute workspace path
    branch        TEXT    NULL                   -- git branch at session start
);

CREATE INDEX IF NOT EXISTS ix_sessions_status     ON sessions(status);
CREATE INDEX IF NOT EXISTS ix_sessions_started_at ON sessions(started_at);
```

One row per chat. Owned by the orchestrator; hooks call `EnsureSession()` to lazily insert.

### `agent_messages`

```36:47:.claude/db/schema.sql
CREATE TABLE IF NOT EXISTS agent_messages (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id  TEXT    NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    agent_name  TEXT    NOT NULL,          -- orchestrator | backend-developer | tester | ...
    role        TEXT    NOT NULL,          -- 'request' | 'response' | 'handoff' | 'finding'
    target      TEXT    NULL,              -- when role=handoff, the receiving agent
    content     TEXT    NOT NULL,          -- may be markdown
    ts          TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_agent_messages_session ON agent_messages(session_id, ts);
CREATE INDEX IF NOT EXISTS ix_agent_messages_agent   ON agent_messages(agent_name, ts);
```

Significant inter-agent exchanges. The `beforeTurn` hook rehydrates these into the parent agent's context.

### `plans`

```53:65:.claude/db/schema.sql
CREATE TABLE IF NOT EXISTS plans (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id  TEXT    NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    title       TEXT    NOT NULL,
    path        TEXT    NOT NULL,           -- path to the .plan.md file on disk
    body_md     TEXT    NOT NULL,
    status      TEXT    NOT NULL DEFAULT 'proposed',   -- proposed | approved | in_progress | done | superseded
    created_by  TEXT    NOT NULL DEFAULT 'orchestrator',
    ts          TEXT    NOT NULL
);
```

Snapshots of plan-mode artefacts. Mirrored by `afterPlanCreation`.

### `decisions`

```70:78:.claude/db/schema.sql
CREATE TABLE IF NOT EXISTS decisions (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id  TEXT    NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    topic       TEXT    NOT NULL,
    choice      TEXT    NOT NULL,
    rationale   TEXT    NOT NULL,
    agent_name  TEXT    NOT NULL,
    ts          TEXT    NOT NULL
);
```

Lightweight ADRs — any agent may append.

### `mistakes`

```86:96:.claude/db/schema.sql
CREATE TABLE IF NOT EXISTS mistakes (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id   TEXT    NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    agent_name   TEXT    NOT NULL,
    category     TEXT    NOT NULL,        -- build | test | review | dispatch | source-gen | other
    summary      TEXT    NOT NULL,
    root_cause   TEXT    NULL,
    fix          TEXT    NULL,
    lesson_file  TEXT    NULL,            -- relative path to .github/Lessons/*.md
    ts           TEXT    NOT NULL
);
```

Populated by `onAgentError`. Each row should eventually reference a `.github/Lessons/*.md` file.

### `reviews`

```105:115:.claude/db/schema.sql
CREATE TABLE IF NOT EXISTS reviews (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id      TEXT    NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    target          TEXT    NOT NULL,     -- file path or commit ref
    severity        TEXT    NOT NULL,     -- Critical | High | Medium | Low | Info
    finding         TEXT    NOT NULL,
    suggested_fix   TEXT    NULL,
    reviewer_agent  TEXT    NOT NULL DEFAULT 'code-reviewer',
    diff_hash       TEXT    NULL,         -- SHA-256 of the reviewed diff; used for cache invalidation
    ts              TEXT    NOT NULL
);
```

Code-reviewer findings. The `autoreview` hook uses `diff_hash` to cache reviews.

### `sprint_backlog`

```123:132:.claude/db/schema.sql
CREATE TABLE IF NOT EXISTS sprint_backlog (
    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id           TEXT    NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    story                TEXT    NOT NULL,
    acceptance_criteria  TEXT    NOT NULL,
    status               TEXT    NOT NULL DEFAULT 'todo',   -- todo | in_progress | review | done | blocked
    assigned_agent       TEXT    NULL,
    priority             INTEGER NOT NULL DEFAULT 3,         -- 1 high … 5 low
    ts                   TEXT    NOT NULL
);
```

Scrum-master's work queue.

### `hook_events`

```140:149:.claude/db/schema.sql
CREATE TABLE IF NOT EXISTS hook_events (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id   TEXT    NULL REFERENCES sessions(id) ON DELETE SET NULL,
    hook_name    TEXT    NOT NULL,        -- file name of the .csx script
    event_type   TEXT    NOT NULL,        -- beforeCommit | beforePush | onAgentError | ...
    outcome      TEXT    NOT NULL,        -- ok | fail | skip
    duration_ms  INTEGER NOT NULL DEFAULT 0,
    payload_json TEXT    NULL,
    ts           TEXT    NOT NULL
);
```

Audit trail. `session_id` is nullable (`ON DELETE SET NULL`) so hook events survive session GC.

## Helper method reference

All helpers live on `public static class ContextDb` in [.claude/lib/ContextDb.csx](.claude/lib/ContextDb.csx). Every example below is a complete `.csx` file you can run with `dotnet script` from the repo root.

### `EnsureSession()`

```110:130:.claude/lib/ContextDb.csx
    public static string EnsureSession(string? userRequest = null, string? branch = null)
    {
        var sid = Environment.GetEnvironmentVariable("MEDIATORLITE_SESSION_ID");
        if (!string.IsNullOrWhiteSpace(sid) && SessionExists(sid!)) return sid!;

        sid = Guid.NewGuid().ToString("N");
```

```csx
#load ".claude/lib/ContextDb.csx"
var sid = ContextDb.EnsureSession(userRequest: "Refactor SourceGeneratedMediator");
Console.WriteLine($"session = {sid}");
```

### `WriteMessage(agent, role, content, target?)`

```csx
#load ".claude/lib/ContextDb.csx"
ContextDb.WriteMessage(
    agent:  "backend-developer",
    role:   "handoff",
    target: "tester",
    content: "Added test hooks to Mediator.SendAsync; please add xUnit coverage for cancellation path.");
```

Roles used in practice: `request`, `response`, `handoff`, `finding`.

### `ReadRecent(limit = 20, sessionId?)`

```csx
#load ".claude/lib/ContextDb.csx"
foreach (var m in ContextDb.ReadRecent(10))
    Console.WriteLine($"{m.Ts} [{m.Agent}/{m.Role}] {m.Content}");
```

Returns the last N rows for the current session (ordered ascending after an internal `.Reverse()` so the oldest is first).

### `SnapshotPlan(title, path, body, createdBy?, status?)`

```csx
#load ".claude/lib/ContextDb.csx"
using System.IO;
var path = ".claude/plans/compile-time_logging_tracing_migration_c8fb2d62.plan.md";
ContextDb.SnapshotPlan(
    title:     "Compile-time logging/tracing migration",
    path:      path,
    body:      File.ReadAllText(path),
    createdBy: "orchestrator",
    status:    "approved");
```

### `LogDecision(topic, choice, rationale, agent?)`

```csx
#load ".claude/lib/ContextDb.csx"
ContextDb.LogDecision(
    topic:     "notification-strategy",
    choice:    "Sequential",
    rationale: "No perf wins from parallel at current handler count.",
    agent:     "dotnet-self-learning-architect");
```

### `LogMistake(agent, category, summary, rootCause?, fix?, lessonFile?)`

```csx
#load ".claude/lib/ContextDb.csx"
var id = ContextDb.LogMistake(
    agent:      "backend-developer",
    category:   "build",
    summary:    "Warnings-as-errors tripped on nullable ref return in SourceGeneratedMediator",
    rootCause:  "Missed non-null assertion on the resolved handler.",
    fix:        "Added `!` after GetRequiredService<IRequestHandler<,>>().",
    lessonFile: ".github/Lessons/2026-04-18-nullable-dispatcher.md");
Console.WriteLine($"mistake row id = {id}");
```

### `LogReview(target, severity, finding, suggestedFix?, diffHash?, reviewer?)`

```csx
#load ".claude/lib/ContextDb.csx"
ContextDb.LogReview(
    target:       "src/MediatorLite/Internal/ThrowingMediator.cs",
    severity:     "Medium",
    finding:      "PublishAsync swallows OperationCanceledException on sequential path.",
    suggestedFix: "Rethrow when cancellationToken.IsCancellationRequested",
    diffHash:     "sha256-abc123…");
```

### `HasFreshReview(diffHash, window)`

```csx
#load ".claude/lib/ContextDb.csx"
var fresh = ContextDb.HasFreshReview("sha256-abc123…", TimeSpan.FromHours(6));
Console.WriteLine(fresh ? "cached review still valid" : "need a fresh review");
```

Used by the autoreview hook to avoid re-reviewing an unchanged diff within a time window.

### `LogHookEvent(hookName, eventType, outcome, durationMs?, payload?)`

```csx
#load ".claude/lib/ContextDb.csx"
var sw = System.Diagnostics.Stopwatch.StartNew();
try
{
    // ... hook body ...
    ContextDb.LogHookEvent("05-inject-context.csx", "beforeTurn", "ok", sw.ElapsedMilliseconds);
}
catch (Exception ex)
{
    ContextDb.LogHookEvent("05-inject-context.csx", "beforeTurn", "fail",
        sw.ElapsedMilliseconds, new { error = ex.Message });
    throw;
}
```

`payload` is serialised via `System.Text.Json.JsonSerializer.Serialize`.

### `AddBacklogItem(story, acceptance, assignedAgent?, priority?)`

```csx
#load ".claude/lib/ContextDb.csx"
var id = ContextDb.AddBacklogItem(
    story:         "Add cancellation-token propagation tests for PublishAsync",
    acceptance:    "Given a cancelled token, PublishAsync must throw OperationCanceledException before the second handler runs.",
    assignedAgent: "tester",
    priority:      2);
Console.WriteLine($"backlog item {id}");
```

### `Vacuum()` / `CloseSession(id, status?)`

```csx
#load ".claude/lib/ContextDb.csx"
ContextDb.CloseSession(Environment.GetEnvironmentVariable("MEDIATORLITE_SESSION_ID")!, status: "closed");
ContextDb.Vacuum();
```

Run only at the end of long sessions — `VACUUM` rewrites the file.

## Subagent pattern: one-shot query

A subagent spawned via the Task tool receives a minimal prompt. If you need it to pull the last N messages to ground its work, include this snippet in its brief:

```csx
#load ".claude/lib/ContextDb.csx"
foreach (var m in ContextDb.ReadRecent(10))
    Console.WriteLine($"{m.Ts} {m.Agent} {m.Role}: {m.Content}");
```

Because `EnsureSession()` reads `MEDIATORLITE_SESSION_ID` from the environment, parent and subagent share the same session as long as the env var is propagated. The helper will *not* create a new session accidentally if the var is set and the row exists.

## Common tasks

1. **Bootstrap the DB from a clean workspace.**
   Any helper call (`EnsureSession()` is the simplest) will create `.claude/db/session.sqlite` and apply the schema.

2. **Append a handoff between agents.**
   `ContextDb.WriteMessage("backend-developer", "handoff", "...", target: "tester")`.

3. **Record an architecture decision.**
   `ContextDb.LogDecision("error-aggregation", "AggregateException", "Matches .NET Task.WhenAll semantics.")`.

4. **Gate an autoreview on diff hash.**
   ```csx
   if (!ContextDb.HasFreshReview(diffHash, TimeSpan.FromHours(2)))
   {
       // spawn code-reviewer subagent
   }
   ```

5. **Audit every decision made in this session.**
   ```csx
   using Microsoft.Data.Sqlite;
   using var c = new SqliteConnection(ContextDb.ConnectionString);
   c.Open();
   using var cmd = c.CreateCommand();
   cmd.CommandText = "SELECT topic, choice, agent_name, ts FROM decisions WHERE session_id = $s ORDER BY ts";
   cmd.Parameters.AddWithValue("$s", ContextDb.EnsureSession());
   using var r = cmd.ExecuteReader();
   while (r.Read()) Console.WriteLine($"{r.GetString(3)} [{r.GetString(2)}] {r.GetString(0)} → {r.GetString(1)}");
   ```

## Pitfalls

- **Creating a new session mid-hook.** If `MEDIATORLITE_SESSION_ID` is missing, `EnsureSession()` allocates a new UUID. Downstream rows end up on the wrong session. Always ensure the top-level orchestrator set the env var before spawning subagents.
- **Opening raw `SqliteConnection` without `PRAGMA foreign_keys = ON`.** You will happily insert a child row with a bogus `session_id`; later queries will return stale data. Always go through `ContextDb.OpenConnection` (private) — or call the high-level helper methods directly.
- **Inserting local time.** SQLite sorts ISO-8601 strings lexicographically; local times with offsets or no `Z` break `ORDER BY ts`. Always pass `DateTime.UtcNow.ToString("o")` (the helpers do this).
- **Treating `session_id` as optional on non-`hook_events` tables.** Only `hook_events.session_id` is nullable. Every other child table will hard-fail on a null FK.
- **Leaving connections open.** All helpers use `using`-scoped connections. If you add a new helper, do the same — SQLite WAL checkpoints require connection closure.
- **Writing to the DB from multiple processes concurrently.** WAL helps with concurrent **readers**, but there is still only one writer. If you ever parallelise subagents across processes, serialise writes (or add a simple retry-on-busy).
- **Migrating columns via helpers.** `schema.sql` uses `CREATE TABLE IF NOT EXISTS` — adding a column there will not alter existing tables. Write a dedicated migration `.csx` and run it once, then update `schema.sql`.
- **Trusting `Trim(...)`.** Content is truncated at 10,000 chars (messages) / 2,000 chars (user_request) / 4,000 chars (mistake summary) with a trailing `…`. Do not rely on full-fidelity storage for long artefacts — snapshot them as files and store the path instead.

## Related

- [.claude/db/schema.sql](.claude/db/schema.sql) — source of truth for the table shape.
- [.claude/lib/ContextDb.csx](.claude/lib/ContextDb.csx) — the only helper you should use.
- [.github/agents/dotnet-self-learning-architect.agent.md](.github/agents/dotnet-self-learning-architect.agent.md) — role that owns the session lifecycle.
- [.claude/skills/agentic-workflow/SKILL.md](.claude/skills/agentic-workflow/SKILL.md) — shows how these helpers plug into the multi-agent flow.
