#!/usr/bin/env dotnet-script
#nullable enable
// .claude/hooks/30-test-gate.csx
// Event: beforePush
// Purpose: refuse to push if `dotnet test` fails for any project in the solution.
//
// Notes:
//   * Assumes the build gate already ran locally before push (beforeCommit chain).
//   * Runs a plain `dotnet test MediatorLite.sln -c Release` (restore included).
//   * Benchmarks are not run (they're BenchmarkDotNet programs, not xUnit tests).
//   * If the dotnet SDK is missing, warns and allows the push (tests didn't fail —
//     they couldn't run); override discipline is the same as the other gates.

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
(int exit, string stdout, string stderr) testResult;
try
{
    testResult = Run("dotnet", "test MediatorLite.sln -c Release --nologo -v q");
}
catch (Exception ex)
{
    // Fail open: a machine without the SDK cannot run tests, which is not the same as
    // tests failing. Crashing here used to hard-block the push with no explanation.
    Console.Error.WriteLine($"[hook:30-test-gate] WARN — could not run dotnet ({ex.Message}); allowing push unverified");
    ContextDb.LogHookEvent("30-test-gate.csx", "beforePush", "warn", sw.ElapsedMilliseconds, new { reason = "dotnet-missing" });
    return;
}
var (exit, stdout, stderr) = testResult;
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
    // Drain both pipes concurrently: reading stdout to EOF while the child blocks on a
    // full stderr pipe buffer (or vice versa) deadlocks both processes.
    var so = p.StandardOutput.ReadToEndAsync();
    var se = p.StandardError.ReadToEndAsync();
    System.Threading.Tasks.Task.WaitAll(so, se);
    p.WaitForExit();
    return (p.ExitCode, so.Result, se.Result);
}

static string Tail(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(s.Length - n));
