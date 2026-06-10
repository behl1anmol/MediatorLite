#!/usr/bin/env dotnet-script
// .agents/scripts/log-mistake.csx
// Logs a mistake and returns the ID.
// Usage: dotnet-script .agents/scripts/log-mistake.csx <agent> <category> <summary> [rootCause] [fix] [lessonFile]

#load "../lib/ContextDb.csx"

if (Args.Count < 3) {
    Console.WriteLine("Usage: dotnet-script log-mistake.csx <agent> <category> <summary> [rootCause] [fix] [lessonFile]");
    return;
}

var agent = Args[0];
var category = Args[1];
var summary = Args[2];
var rootCause = Args.Count > 3 ? Args[3] : null;
var fix = Args.Count > 4 ? Args[4] : null;
var lessonFile = Args.Count > 5 ? Args[5] : null;

// Attempt to load session ID from .agents/session.env
var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".agents", "session.env");
if (File.Exists(envFile)) {
    foreach (var line in File.ReadAllLines(envFile)) {
        if (line.StartsWith("MEDIATORLITE_SESSION_ID=")) {
            Environment.SetEnvironmentVariable("MEDIATORLITE_SESSION_ID", line.Split('=')[1].Trim());
        }
    }
}

var id = ContextDb.LogMistake(agent, category, summary, rootCause, fix, lessonFile);
Console.WriteLine($"Mistake {id} logged.");
