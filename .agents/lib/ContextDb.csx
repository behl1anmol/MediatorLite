#!/usr/bin/env dotnet-script
#nullable enable
// .cursor/lib/ContextDb.csx
// Shared SQLite helper for every MediatorLite hook and agent tool.
//
// Usage from any hook:
//
//     #load "../lib/ContextDb.csx"
//     using static ContextDb;
//
//     var sid = EnsureSession();
//     WriteMessage("backend-developer", "response", "Refactored Mediator.SendAsync for ...");
//     LogDecision("notification-strategy", "Sequential", "Default, no perf wins from parallel in this bench.");
//     LogMistake("backend-developer", "build", "Treat-warnings-as-errors tripped on nullable ref.", rootCause: "Missed !");
//
// Design notes:
//   * Uses Microsoft.Data.Sqlite (first-party, pure managed, no native build step).
//   * Schema is idempotent: runs .cursor/db/schema.sql on every call (CREATE IF NOT EXISTS).
//   * Session ID is resolved from the MEDIATORLITE_SESSION_ID env var, else generated on-demand.
//   * All timestamps are ISO-8601 UTC ("o" format).

#r "nuget: Microsoft.Data.Sqlite, 9.0.0"

using System;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

public static class ContextDb
{
    private static readonly object _lock = new();
    private static string? _connectionString;
    private static string? _dbPath;
    private static string? _schemaPath;

    public static string DbPath
    {
        get
        {
            EnsureInitialised();
            return _dbPath!;
        }
    }

    public static string ConnectionString
    {
        get
        {
            EnsureInitialised();
            return _connectionString!;
        }
    }

    private static void EnsureInitialised()
    {
        if (_connectionString is not null) return;
        lock (_lock)
        {
            if (_connectionString is not null) return;

            var repoRoot = FindRepoRoot();
            var cursorDir = Path.Combine(repoRoot, ".cursor");
            var dbDir = Path.Combine(cursorDir, "db");
            Directory.CreateDirectory(dbDir);
            _dbPath = Path.Combine(dbDir, "session.sqlite");
            _schemaPath = Path.Combine(dbDir, "schema.sql");
            // Pooling=False is required, not a tuning choice. With pooling on, Microsoft.Data.Sqlite
            // keeps connections alive and closes them from a ProcessExit handler. Under dotnet-script
            // the native e_sqlite3 resolver is already torn down by then, so sqlite3_close_v2 throws
            // DllNotFoundException and the hook exits 134 — which fails the gate and blocks the
            // command it was guarding. Closing on Dispose keeps every close inside the run.
            _connectionString = $"Data Source={_dbPath};Cache=Shared;Pooling=False";

            // Apply schema (idempotent).
            if (File.Exists(_schemaPath))
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = File.ReadAllText(_schemaPath);
                cmd.ExecuteNonQuery();
            }
        }
    }

    private static string FindRepoRoot()
    {
        // Walk upward from CWD until we find a .cursor/ directory or .git/.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".cursor"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))    return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private static string NowUtc() => DateTime.UtcNow.ToString("o");

    private static SqliteConnection OpenConnection()
    {
        EnsureInitialised();
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
            pragma.ExecuteNonQuery();
        }
        return conn;
    }

    // ─────────────────────────────── Sessions ──────────────────────────────

    public static string EnsureSession(string? userRequest = null, string? branch = null)
    {
        var sid = Environment.GetEnvironmentVariable("MEDIATORLITE_SESSION_ID");
        if (!string.IsNullOrWhiteSpace(sid) && SessionExists(sid!)) return sid!;

        sid = Guid.NewGuid().ToString("N");
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions (id, started_at, user_request, status, workspace, branch)
            VALUES ($id, $started, $req, 'active', $ws, $branch);
        """;
        cmd.Parameters.AddWithValue("$id", sid);
        cmd.Parameters.AddWithValue("$started", NowUtc());
        cmd.Parameters.AddWithValue("$req", Trim(userRequest, 2000) ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ws", FindRepoRoot());
        cmd.Parameters.AddWithValue("$branch", (object?)branch ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        Environment.SetEnvironmentVariable("MEDIATORLITE_SESSION_ID", sid);
        return sid;
    }

    public static bool SessionExists(string id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sessions WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() is not null;
    }

    public static void CloseSession(string id, string status = "closed")
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET ended_at = $end, status = $st WHERE id = $id;";
        cmd.Parameters.AddWithValue("$end", NowUtc());
        cmd.Parameters.AddWithValue("$st", status);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ─────────────────────────────── Messages ──────────────────────────────

    public static void WriteMessage(string agent, string role, string content, string? target = null)
    {
        var sid = EnsureSession();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agent_messages (session_id, agent_name, role, target, content, ts)
            VALUES ($sid, $agent, $role, $target, $content, $ts);
        """;
        cmd.Parameters.AddWithValue("$sid", sid);
        cmd.Parameters.AddWithValue("$agent", agent);
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$target", (object?)target ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$content", Trim(content, 10_000) ?? "");
        cmd.Parameters.AddWithValue("$ts", NowUtc());
        cmd.ExecuteNonQuery();
    }

    public record AgentMessage(long Id, string Agent, string Role, string? Target, string Content, string Ts);

    public static List<AgentMessage> ReadRecent(int limit = 20, string? sessionId = null)
    {
        var sid = sessionId ?? EnsureSession();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, agent_name, role, target, content, ts
            FROM   agent_messages
            WHERE  session_id = $sid
            ORDER  BY ts DESC
            LIMIT  $n;
        """;
        cmd.Parameters.AddWithValue("$sid", sid);
        cmd.Parameters.AddWithValue("$n", limit);
        var list = new List<AgentMessage>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new AgentMessage(
                r.GetInt64(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4), r.GetString(5)));
        }
        list.Reverse();
        return list;
    }

    // ─────────────────────────────── Plans ─────────────────────────────────

    public static void SnapshotPlan(string title, string path, string body, string createdBy = "orchestrator", string status = "proposed")
    {
        var sid = EnsureSession();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO plans (session_id, title, path, body_md, status, created_by, ts)
            VALUES ($sid, $t, $p, $b, $st, $by, $ts);
        """;
        cmd.Parameters.AddWithValue("$sid", sid);
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$p", path);
        cmd.Parameters.AddWithValue("$b", body);
        cmd.Parameters.AddWithValue("$st", status);
        cmd.Parameters.AddWithValue("$by", createdBy);
        cmd.Parameters.AddWithValue("$ts", NowUtc());
        cmd.ExecuteNonQuery();
    }

    // ───────────────────────── Decisions & Mistakes ────────────────────────

    public static void LogDecision(string topic, string choice, string rationale, string agent = "orchestrator")
    {
        var sid = EnsureSession();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO decisions (session_id, topic, choice, rationale, agent_name, ts)
            VALUES ($sid, $t, $c, $r, $a, $ts);
        """;
        cmd.Parameters.AddWithValue("$sid", sid);
        cmd.Parameters.AddWithValue("$t", topic);
        cmd.Parameters.AddWithValue("$c", choice);
        cmd.Parameters.AddWithValue("$r", rationale);
        cmd.Parameters.AddWithValue("$a", agent);
        cmd.Parameters.AddWithValue("$ts", NowUtc());
        cmd.ExecuteNonQuery();
    }

    public static long LogMistake(string agent, string category, string summary,
                                   string? rootCause = null, string? fix = null, string? lessonFile = null)
    {
        var sid = EnsureSession();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mistakes (session_id, agent_name, category, summary, root_cause, fix, lesson_file, ts)
            VALUES ($sid, $a, $cat, $s, $rc, $fx, $lf, $ts);
            SELECT last_insert_rowid();
        """;
        cmd.Parameters.AddWithValue("$sid", sid);
        cmd.Parameters.AddWithValue("$a", agent);
        cmd.Parameters.AddWithValue("$cat", category);
        cmd.Parameters.AddWithValue("$s", Trim(summary, 4000) ?? "");
        cmd.Parameters.AddWithValue("$rc", (object?)rootCause ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fx", (object?)fix ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lf", (object?)lessonFile ?? DBNull.Value);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    // ─────────────────────────────── Reviews ───────────────────────────────

    public static void LogReview(string target, string severity, string finding,
                                  string? suggestedFix = null, string? diffHash = null, string reviewer = "code-reviewer")
    {
        var sid = EnsureSession();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO reviews (session_id, target, severity, finding, suggested_fix, reviewer_agent, diff_hash, ts)
            VALUES ($sid, $t, $sev, $f, $sf, $rev, $dh, $ts);
        """;
        cmd.Parameters.AddWithValue("$sid", sid);
        cmd.Parameters.AddWithValue("$t", target);
        cmd.Parameters.AddWithValue("$sev", severity);
        cmd.Parameters.AddWithValue("$f", finding);
        cmd.Parameters.AddWithValue("$sf", (object?)suggestedFix ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rev", reviewer);
        cmd.Parameters.AddWithValue("$dh", (object?)diffHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", NowUtc());
        cmd.ExecuteNonQuery();
    }

    public static bool HasFreshReview(string diffHash, TimeSpan window)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM reviews
            WHERE  diff_hash = $h
              AND  ts >= $threshold
            LIMIT  1;
        """;
        cmd.Parameters.AddWithValue("$h", diffHash);
        cmd.Parameters.AddWithValue("$threshold", DateTime.UtcNow.Subtract(window).ToString("o"));
        return cmd.ExecuteScalar() is not null;
    }

    // ────────────────────────────── Hook events ────────────────────────────

    public static void LogHookEvent(string hookName, string eventType, string outcome,
                                     long durationMs = 0, object? payload = null)
    {
        var sid = Environment.GetEnvironmentVariable("MEDIATORLITE_SESSION_ID");
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO hook_events (session_id, hook_name, event_type, outcome, duration_ms, payload_json, ts)
            VALUES ($sid, $h, $et, $oc, $dur, $pl, $ts);
        """;
        cmd.Parameters.AddWithValue("$sid", (object?)sid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$h", hookName);
        cmd.Parameters.AddWithValue("$et", eventType);
        cmd.Parameters.AddWithValue("$oc", outcome);
        cmd.Parameters.AddWithValue("$dur", durationMs);
        cmd.Parameters.AddWithValue("$pl", payload is null ? (object)DBNull.Value : JsonSerializer.Serialize(payload));
        cmd.Parameters.AddWithValue("$ts", NowUtc());
        cmd.ExecuteNonQuery();
    }

    // ────────────────────────────── Backlog ────────────────────────────────

    public static long AddBacklogItem(string story, string acceptance, string? assignedAgent = null, int priority = 3)
    {
        var sid = EnsureSession();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sprint_backlog (session_id, story, acceptance_criteria, assigned_agent, priority, ts)
            VALUES ($sid, $s, $ac, $a, $p, $ts);
            SELECT last_insert_rowid();
        """;
        cmd.Parameters.AddWithValue("$sid", sid);
        cmd.Parameters.AddWithValue("$s", story);
        cmd.Parameters.AddWithValue("$ac", acceptance);
        cmd.Parameters.AddWithValue("$a", (object?)assignedAgent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$p", priority);
        cmd.Parameters.AddWithValue("$ts", NowUtc());
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    // ─────────────────────────────── Utilities ─────────────────────────────

    public static void Vacuum()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "VACUUM;";
        cmd.ExecuteNonQuery();
    }

    private static string? Trim(string? s, int max) =>
        string.IsNullOrEmpty(s) ? null : (s!.Length <= max ? s : s.Substring(0, max) + "…");
}
