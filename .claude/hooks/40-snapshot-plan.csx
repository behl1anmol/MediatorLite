#!/usr/bin/env dotnet-script
#nullable enable
// .claude/hooks/40-snapshot-plan.csx
// Event: afterPlanCreation (Cursor) / PostToolUse on ExitPlanMode or TodoWrite (Claude Code)
// Purpose: mirror every plan file under .claude/plans/ into the `plans` table
//          so the Scrum Master agent (and any other) can query recent plans.
//
// Payload: Cursor writes plans to .claude/plans/<slug>_<hash>.plan.md. We do not
// receive a stdin payload reliably across builds, so this hook walks .claude/plans/
// and ingests any plan file newer than the most recent row in `plans` for this session.

#load "../lib/ContextDb.csx"

using System;
using System.IO;
using System.Linq;

try
{
    var repoRoot = FindRepoRoot();
    var plansDir = Path.Combine(repoRoot, ".claude", "plans");
    if (!Directory.Exists(plansDir))
    {
        ContextDb.LogHookEvent("40-snapshot-plan.csx", "afterPlanCreation", "skip", 0, new { reason = "no-plans-dir" });
        return;
    }

    var sid = ContextDb.EnsureSession();
    var ingested = 0;
    foreach (var file in Directory.GetFiles(plansDir, "*.plan.md").OrderBy(File.GetLastWriteTimeUtc))
    {
        var rel = Path.GetRelativePath(repoRoot, file);
        // Dedupe: skip if this path already recorded for the current session.
        if (AlreadyRecorded(sid, rel)) continue;

        var body = File.ReadAllText(file);
        var title = ExtractTitle(body) ?? Path.GetFileNameWithoutExtension(file);
        ContextDb.SnapshotPlan(title: title, path: rel, body: body, createdBy: "orchestrator", status: "proposed");
        ingested++;
    }

    Console.WriteLine($"[hook:40-snapshot-plan] ingested={ingested}");
    ContextDb.LogHookEvent("40-snapshot-plan.csx", "afterPlanCreation", "ok", 0, new { ingested });
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[hook:40-snapshot-plan] warn: {ex.Message}");
}

static string? ExtractTitle(string md)
{
    foreach (var l in md.Split('\n'))
    {
        var t = l.TrimStart();
        if (t.StartsWith("# ")) return t.Substring(2).Trim();
        if (t.StartsWith("name:")) return t.Substring(5).Trim(' ', '"', '\'', '\r');
    }
    return null;
}

static bool AlreadyRecorded(string sid, string relPath)
{
    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(ContextDb.ConnectionString);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT 1 FROM plans WHERE session_id = $s AND path = $p LIMIT 1;";
    cmd.Parameters.AddWithValue("$s", sid);
    cmd.Parameters.AddWithValue("$p", relPath);
    return cmd.ExecuteScalar() is not null;
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
        dir = dir.Parent;
    }
    return Directory.GetCurrentDirectory();
}
