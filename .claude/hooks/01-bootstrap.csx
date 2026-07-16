#!/usr/bin/env dotnet-script
#nullable enable
// .claude/hooks/01-bootstrap.csx
// Event: onSessionStart
// Purpose: one-time per-session setup.
//   1. Ensure .claude/db/session.sqlite exists with the schema applied
//      (ContextDb.csx does this lazily on first call).
//   2. Create a new sessions row, bind MEDIATORLITE_SESSION_ID for downstream hooks.
//   3. Capture the current git branch into the session row.
//   4. Log the hook event.
//
// Must never throw — session bootstrap failures should be warnings, not fatal.

#load "../lib/ContextDb.csx"

using System;
using System.Diagnostics;
using System.IO;

var sw = Stopwatch.StartNew();
try
{
    var branch = TryRunGit("rev-parse --abbrev-ref HEAD")?.Trim();
    var sid = ContextDb.EnsureSession(userRequest: null, branch: branch);
    Console.WriteLine($"[hook:01-bootstrap] session={sid} branch={branch ?? "(none)"} db={ContextDb.DbPath}");
    ContextDb.LogHookEvent("01-bootstrap.csx", "onSessionStart", "ok", sw.ElapsedMilliseconds,
        new { sid, branch });
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[hook:01-bootstrap] warn: {ex.Message}");
    try { ContextDb.LogHookEvent("01-bootstrap.csx", "onSessionStart", "fail", sw.ElapsedMilliseconds, new { error = ex.Message }); } catch { }
}

static string? TryRunGit(string args)
{
    try
    {
        var psi = new ProcessStartInfo("git", args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi); if (p is null) return null;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(2000);
        return p.ExitCode == 0 ? output : null;
    }
    catch { return null; }
}
