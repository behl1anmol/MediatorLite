#!/usr/bin/env dotnet-script
// .claude/hooks/30-test-gate.csx
// Event: beforePush
// Purpose: refuse to push if `dotnet test` fails for any project in the solution.
//
// Notes:
//   * Assumes the build gate already ran locally before push (beforeCommit chain).
//   * Uses --no-restore to minimise time but falls back to a full test if that fails.
//   * Benchmarks are not run (they're BenchmarkDotNet programs, not xUnit tests).

#load "../lib/ContextDb.csx"

using System;
using System.Diagnostics;

var sw = Stopwatch.StartNew();

if (Environment.GetEnvironmentVariable("MEDIATORLITE_SKIP_TESTS") == "1")
{
    Console.WriteLine("[hook:30-test-gate] skipped (env override)");
    ContextDb.LogHookEvent("30-test-gate.csx", "beforePush", "skip", sw.ElapsedMilliseconds, new { reason = "env-override" });
    return;
}

Console.WriteLine("[hook:30-test-gate] dotnet test MediatorLite.sln -c Release --nologo …");
var (exit, stdout, stderr) = Run("dotnet", "test MediatorLite.sln -c Release --nologo -v q");
if (exit == 0)
{
    Console.WriteLine("[hook:30-test-gate] tests passed");
    ContextDb.LogHookEvent("30-test-gate.csx", "beforePush", "ok", sw.ElapsedMilliseconds, new { });
    return;
}

Console.Error.WriteLine("""

[hook:30-test-gate] BLOCKED — tests failed
==========================================================================
Run `dotnet test MediatorLite.sln` locally and fix failing tests before pushing.
Override (discouraged): $env:MEDIATORLITE_SKIP_TESTS = '1'
==========================================================================
""");
Console.Error.WriteLine(Tail(stdout, 4000));
if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine(Tail(stderr, 2000));
ContextDb.LogHookEvent("30-test-gate.csx", "beforePush", "fail", sw.ElapsedMilliseconds, new { exit });
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

static string Tail(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(s.Length - n));
