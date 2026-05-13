#!/usr/bin/env dotnet-script
// .claude/hooks/21-build-gate.csx
// Event: beforeCommit
// Purpose: refuse to commit if the solution does not build.
//
// Fast-path: skips when the staged diff touches no .cs / .csproj / .props / .targets file.
// Slow-path: `dotnet build MediatorLite.sln -c Release --nologo -v q`.
//
// Exit semantics:
//   0   build succeeded (or skipped)
//   1   build failed — commit blocked
//   2   dotnet SDK not found — warns but allows commit

#load "../lib/ContextDb.csx"

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

var sw = Stopwatch.StartNew();

var staged = RunCapture("git", "diff --staged --name-only") ?? "";
var files = staged.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();
bool codeTouched = files.Any(f =>
    f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)       ||
    f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)   ||
    f.EndsWith(".props", StringComparison.OrdinalIgnoreCase)    ||
    f.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)  ||
    f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
if (!codeTouched)
{
    Console.WriteLine("[hook:21-build-gate] skipped (no code files staged)");
    ContextDb.LogHookEvent("21-build-gate.csx", "beforeCommit", "skip", sw.ElapsedMilliseconds, new { fileCount = files.Length });
    return;
}

Console.WriteLine("[hook:21-build-gate] dotnet build MediatorLite.sln -c Release …");
var (exit, stdout, stderr) = Run("dotnet", "build MediatorLite.sln -c Release --nologo -v q");
if (exit == 0)
{
    Console.WriteLine("[hook:21-build-gate] build OK");
    ContextDb.LogHookEvent("21-build-gate.csx", "beforeCommit", "ok", sw.ElapsedMilliseconds, new { });
    return;
}

Console.Error.WriteLine("""

[hook:21-build-gate] BLOCKED — build failed
==========================================================================
""");
Console.Error.WriteLine(stdout);
if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine(stderr);
Console.Error.WriteLine("==========================================================================");
ContextDb.LogHookEvent("21-build-gate.csx", "beforeCommit", "fail", sw.ElapsedMilliseconds,
    new { exit, stderrTail = Tail(stderr, 1000) });
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

static string Tail(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(s.Length - n));
