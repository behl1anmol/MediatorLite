#!/usr/bin/env dotnet-script
// .claude/hooks/22-lint-gate.csx
// Event: beforeCommit
// Purpose: cheap style/formatting gate before commit.
// Runs `dotnet format MediatorLite.sln --verify-no-changes --no-restore` (no write).
//
// Opt out per-commit via MEDIATORLITE_SKIP_FORMAT=1.
// If `dotnet format` is not installed, warns and allows commit.

#load "../lib/ContextDb.csx"

using System;
using System.Diagnostics;
using System.Linq;

var sw = Stopwatch.StartNew();

if (Environment.GetEnvironmentVariable("MEDIATORLITE_SKIP_FORMAT") == "1")
{
    Console.WriteLine("[hook:22-lint-gate] skipped (env override)");
    ContextDb.LogHookEvent("22-lint-gate.csx", "beforeCommit", "skip", sw.ElapsedMilliseconds, new { reason = "env-override" });
    return;
}

var staged = RunCapture("git", "diff --staged --name-only") ?? "";
var files = staged.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();
if (!files.Any(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("[hook:22-lint-gate] skipped (no .cs staged)");
    ContextDb.LogHookEvent("22-lint-gate.csx", "beforeCommit", "skip", sw.ElapsedMilliseconds, new { fileCount = files.Length });
    return;
}

Console.WriteLine("[hook:22-lint-gate] dotnet format --verify-no-changes …");
var (exit, stdout, stderr) = Run("dotnet", "format MediatorLite.sln --verify-no-changes --no-restore --severity warn");
if (exit == 0)
{
    Console.WriteLine("[hook:22-lint-gate] format OK");
    ContextDb.LogHookEvent("22-lint-gate.csx", "beforeCommit", "ok", sw.ElapsedMilliseconds, new { });
    return;
}

Console.Error.WriteLine("""

[hook:22-lint-gate] BLOCKED — formatting/lint issues
==========================================================================
Run `dotnet format MediatorLite.sln` to fix, then re-stage and commit.
Override (discouraged): $env:MEDIATORLITE_SKIP_FORMAT = '1'
==========================================================================
""");
Console.Error.WriteLine(stdout);
if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine(stderr);
ContextDb.LogHookEvent("22-lint-gate.csx", "beforeCommit", "fail", sw.ElapsedMilliseconds, new { exit });
Environment.Exit(exit == 0 ? 1 : exit);

static (int, string, string) Run(string file, string args)
{
    var psi = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    using var p = Process.Start(psi)!;
    var so = p.StandardOutput.ReadToEnd();
    var se = p.StandardError.ReadToEnd();
    p.WaitForExit();
    return (p.ExitCode, so, se);
}

static string? RunCapture(string file, string args)
{
    try { var (e, so, _) = Run(file, args); return e == 0 ? so : null; } catch { return null; }
}
