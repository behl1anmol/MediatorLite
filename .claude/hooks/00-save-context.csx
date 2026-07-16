#!/usr/bin/env dotnet-script
#nullable enable
// .claude/hooks/00-save-context.csx
// Event: beforeCompaction
// Purpose: durability — dump the current session (sessions/agent_messages/plans/decisions)
// to a JSON snapshot so nothing is lost when Cursor compacts context.
//
// Also:
//   * Marks the session status as 'compacted' (will be flipped back to 'active' by the next beforeTurn).
//   * Runs VACUUM to keep the DB file tidy.

#load "../lib/ContextDb.csx"

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

var sw = Stopwatch.StartNew();
try
{
    var sid = Environment.GetEnvironmentVariable("MEDIATORLITE_SESSION_ID") ?? ContextDb.EnsureSession();

    // Session ids are Guid "N" strings (see ContextDb.EnsureSession), but the env var is
    // external input: validate before it reaches a file name, and parameterize the queries
    // below like every other query in the hook layer.
    if (!System.Text.RegularExpressions.Regex.IsMatch(sid, "^[0-9a-fA-F-]{1,64}$"))
        throw new InvalidOperationException(
            $"MEDIATORLITE_SESSION_ID '{sid}' is not a valid session id; refusing to snapshot.");

    var snapDir = Path.Combine(Path.GetDirectoryName(ContextDb.DbPath)!, "snapshots");
    Directory.CreateDirectory(snapDir);
    var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
    var outPath = Path.Combine(snapDir, $"{sid}-{stamp}.json");

    var snapshot = new Dictionary<string, object?>
    {
        ["session_id"] = sid,
        ["taken_at"]   = DateTime.UtcNow.ToString("o"),
        ["sessions"]   = DumpTable("SELECT * FROM sessions WHERE id = $sid", sid),
        ["messages"]   = DumpTable("SELECT * FROM agent_messages WHERE session_id = $sid ORDER BY id", sid),
        ["plans"]      = DumpTable("SELECT * FROM plans WHERE session_id = $sid ORDER BY id", sid),
        ["decisions"]  = DumpTable("SELECT * FROM decisions WHERE session_id = $sid ORDER BY id", sid),
        ["mistakes"]   = DumpTable("SELECT * FROM mistakes WHERE session_id = $sid ORDER BY id", sid),
        ["reviews"]    = DumpTable("SELECT * FROM reviews WHERE session_id = $sid ORDER BY id", sid),
        ["backlog"]    = DumpTable("SELECT * FROM sprint_backlog WHERE session_id = $sid ORDER BY id", sid)
    };

    File.WriteAllText(outPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));

    using (var conn = new SqliteConnection(ContextDb.ConnectionString))
    {
        conn.Open();
        using var update = conn.CreateCommand();
        update.CommandText = "UPDATE sessions SET status = 'compacted' WHERE id = $id;";
        update.Parameters.AddWithValue("$id", sid);
        update.ExecuteNonQuery();
    }

    ContextDb.Vacuum();
    Console.WriteLine($"[hook:00-save-context] snapshot -> {outPath}");
    ContextDb.LogHookEvent("00-save-context.csx", "beforeCompaction", "ok", sw.ElapsedMilliseconds, new { snapshot = outPath });
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[hook:00-save-context] warn: {ex.Message}");
    try { ContextDb.LogHookEvent("00-save-context.csx", "beforeCompaction", "fail", sw.ElapsedMilliseconds, new { error = ex.Message }); } catch { }
}

static List<Dictionary<string, object?>> DumpTable(string sql, string sid)
{
    var rows = new List<Dictionary<string, object?>>();
    using var conn = new SqliteConnection(ContextDb.ConnectionString);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("$sid", sid);
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        var row = new Dictionary<string, object?>();
        for (int i = 0; i < r.FieldCount; i++)
            row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        rows.Add(row);
    }
    return rows;
}
