-- MediatorLite Agentic Session DB
-- Location: .cursor/db/session.sqlite
-- Owner: orchestrator agent (all role agents may read; write via ContextDb.csx helpers)
--
-- Design notes:
--   * Durable cross-chat context for the MediatorLite workspace.
--   * All timestamps are ISO-8601 UTC strings (SQLite has no native DateTime).
--   * Keys use rowid-based INTEGER PRIMARY KEY for insert speed; sessions.id is TEXT (UUID).
--   * Foreign keys are ON (see PRAGMA in ContextDb.csx).
--   * WAL mode is enabled in the helper for concurrent reads while the agent writes.

PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;

-- ─────────────────────────────────────────────────────────────────────────────
-- sessions: one row per Cursor agent session (chat).
-- ─────────────────────────────────────────────────────────────────────────────
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

-- ─────────────────────────────────────────────────────────────────────────────
-- agent_messages: significant inter-agent exchanges.
-- Written by ContextDb.WriteMessage(agent, role, content) from role agents.
-- Consumed by beforeTurn hook (05-inject-context.csx) to rehydrate cross-chat memory.
-- ─────────────────────────────────────────────────────────────────────────────
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

-- ─────────────────────────────────────────────────────────────────────────────
-- plans: snapshots of plan-mode artefacts. The afterPlanCreation hook mirrors
-- every plan into this table so the Scrum Master can query them.
-- ─────────────────────────────────────────────────────────────────────────────
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

CREATE INDEX IF NOT EXISTS ix_plans_session ON plans(session_id, ts);
CREATE INDEX IF NOT EXISTS ix_plans_status  ON plans(status);

-- ─────────────────────────────────────────────────────────────────────────────
-- decisions: lightweight ADRs. Any agent can call ContextDb.LogDecision.
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS decisions (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id  TEXT    NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    topic       TEXT    NOT NULL,
    choice      TEXT    NOT NULL,
    rationale   TEXT    NOT NULL,
    agent_name  TEXT    NOT NULL,
    ts          TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_decisions_session ON decisions(session_id, ts);

-- ─────────────────────────────────────────────────────────────────────────────
-- mistakes: fed by onAgentError hook (10-log-mistake.csx).
-- Each row ideally produces a matching file under .github/Lessons/.
-- ─────────────────────────────────────────────────────────────────────────────
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

CREATE INDEX IF NOT EXISTS ix_mistakes_session  ON mistakes(session_id, ts);
CREATE INDEX IF NOT EXISTS ix_mistakes_category ON mistakes(category);

-- ─────────────────────────────────────────────────────────────────────────────
-- reviews: code-reviewer findings. autoreview hook (20-autoreview.csx) queries
-- this table to decide whether the diff has a fresh review.
-- ─────────────────────────────────────────────────────────────────────────────
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

CREATE INDEX IF NOT EXISTS ix_reviews_session   ON reviews(session_id, ts);
CREATE INDEX IF NOT EXISTS ix_reviews_diff_hash ON reviews(diff_hash);

-- ─────────────────────────────────────────────────────────────────────────────
-- sprint_backlog: maintained by scrum-master.
-- ─────────────────────────────────────────────────────────────────────────────
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

CREATE INDEX IF NOT EXISTS ix_backlog_session  ON sprint_backlog(session_id, status);
CREATE INDEX IF NOT EXISTS ix_backlog_assigned ON sprint_backlog(assigned_agent);

-- ─────────────────────────────────────────────────────────────────────────────
-- hook_events: append-only audit of every hook invocation.
-- ─────────────────────────────────────────────────────────────────────────────
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

CREATE INDEX IF NOT EXISTS ix_hook_events_session ON hook_events(session_id, ts);
CREATE INDEX IF NOT EXISTS ix_hook_events_hook    ON hook_events(hook_name, ts);
