#!/usr/bin/env dotnet-script
// .agents/scripts/log-decision.csx
// Logs a decision to the session DB.
// Usage: dotnet-script .agents/scripts/log-decision.csx <topic> <choice> <rationale> [agent]

#load "../lib/ContextDb.csx"

if (Args.Count < 3) {
    Console.WriteLine("Usage: dotnet-script log-decision.csx <topic> <choice> <rationale> [agent]");
    return;
}

var topic = Args[0];
var choice = Args[1];
var rationale = Args[2];
var agent = Args.Count > 3 ? Args[3] : "orchestrator";

// Attempt to load session ID from .agents/session.env
var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".agents", "session.env");
if (File.Exists(envFile)) {
    foreach (var line in File.ReadAllLines(envFile)) {
        if (line.StartsWith("MEDIATORLITE_SESSION_ID=")) {
            Environment.SetEnvironmentVariable("MEDIATORLITE_SESSION_ID", line.Split('=')[1].Trim());
        }
    }
}

ContextDb.LogDecision(topic, choice, rationale, agent);
Console.WriteLine($"Decision logged for topic: {topic}");
