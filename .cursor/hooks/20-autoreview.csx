#!/usr/bin/env dotnet-script
// .cursor/hooks/20-autoreview.csx
// Event: beforeCommit (first in the chain)
// Purpose: block the commit if the code-reviewer has not yet reviewed the
//          currently-staged diff within the last 2 hours.
//
// Mechanism:
//   1. Compute SHA-256 of `git diff --staged`.
//   2. Query the `reviews` table for any row with diff_hash = H and ts >= now-2h.
//   3. If none, print an instructive message and exit 1 (blocks commit).
//   4. If the staged diff is markdown-only, skip (no code changed).
//
// Override: set `MEDIATORLITE_SKIP_AUTOREVIEW=1` to bypass (use for docs-only emergencies).

#load "../lib/ContextDb.csx"

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

var sw = Stopwatch.StartNew();

if (Environment.GetEnvironmentVariable("MEDIATORLITE_SKIP_AUTOREVIEW") == "1")
{
    Console.WriteLine("[hook:20-autoreview] skipped (MEDIATORLITE_SKIP_AUTOREVIEW=1)");
    ContextDb.LogHookEvent("20-autoreview.csx", "beforeCommit", "skip", sw.ElapsedMilliseconds, new { reason = "env-override" });
    return;
}

var stagedFiles = RunGit("diff --staged --name-only") ?? "";
var files = stagedFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();
if (files.Length == 0)
{
    ContextDb.LogHookEvent("20-autoreview.csx", "beforeCommit", "skip", sw.ElapsedMilliseconds, new { reason = "no-staged-files" });
    return;
}

var onlyDocs = files.All(f =>
    f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
    f.EndsWith(".mdc", StringComparison.OrdinalIgnoreCase) ||
    f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
    f.StartsWith(".cursor/plans/", StringComparison.OrdinalIgnoreCase));
if (onlyDocs)
{
    Console.WriteLine("[hook:20-autoreview] skipped (docs-only change)");
    ContextDb.LogHookEvent("20-autoreview.csx", "beforeCommit", "skip", sw.ElapsedMilliseconds, new { reason = "docs-only", fileCount = files.Length });
    return;
}

var diff = RunGit("diff --staged") ?? "";
var hash = Sha256(diff);

if (ContextDb.HasFreshReview(hash, TimeSpan.FromHours(2)))
{
    Console.WriteLine("[hook:20-autoreview] review cached; commit allowed");
    ContextDb.LogHookEvent("20-autoreview.csx", "beforeCommit", "ok", sw.ElapsedMilliseconds, new { hash, cached = true });
    return;
}

Console.Error.WriteLine("""

[hook:20-autoreview] BLOCKED
==========================================================================
No fresh code review found for the currently-staged diff
(diff_hash matches nothing in .cursor/db/session.sqlite::reviews within 2h).

Run the code-reviewer agent first. When it completes, it will write the
finding to the `reviews` table tagged with this diff hash.

Quick review path:
  1. Invoke the code-reviewer agent (e.g. via the orchestrator or directly).
  2. Provide the staged diff: `git diff --staged | pbcopy` / `clip.exe`.
  3. Have the reviewer log findings via ContextDb.LogReview(..., diffHash).

Bypass (emergency only):
  $env:MEDIATORLITE_SKIP_AUTOREVIEW = '1'; git commit ...
==========================================================================
""");
ContextDb.LogHookEvent("20-autoreview.csx", "beforeCommit", "fail", sw.ElapsedMilliseconds, new { hash, reason = "no-fresh-review", fileCount = files.Length });
Environment.Exit(1);

static string? RunGit(string args)
{
    try
    {
        var psi = new ProcessStartInfo("git", args) { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi); if (p is null) return null;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(10_000);
        return p.ExitCode == 0 ? output : null;
    }
    catch { return null; }
}

static string Sha256(string s)
{
    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}
