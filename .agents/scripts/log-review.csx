#!/usr/bin/env dotnet-script
// .agents/scripts/log-review.csx
// Logs a code review finding.
// Usage: dotnet-script .agents/scripts/log-review.csx <target> <severity> <finding> [suggestedFix] [diffHash] [reviewer]

#load "../lib/ContextDb.csx"

if (Args.Count < 3) {
    Console.WriteLine("Usage: dotnet-script log-review.csx <target> <severity> <finding> [suggestedFix] [diffHash] [reviewer]");
    return;
}

var target = Args[0];
var severity = Args[1];
var finding = Args[2];
var suggestedFix = Args.Count > 3 ? Args[3] : null;
var diffHash = Args.Count > 4 ? Args[4] : null;
var reviewer = Args.Count > 5 ? Args[5] : "code-reviewer";

// Attempt to load session ID from .agents/session.env
var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".agents", "session.env");
if (File.Exists(envFile)) {
    foreach (var line in File.ReadAllLines(envFile)) {
        if (line.StartsWith("MEDIATORLITE_SESSION_ID=")) {
            Environment.SetEnvironmentVariable("MEDIATORLITE_SESSION_ID", line.Split('=')[1].Trim());
        }
    }
}

ContextDb.LogReview(target, severity, finding, suggestedFix, diffHash, reviewer);
Console.WriteLine($"Review finding logged for {target}.");
