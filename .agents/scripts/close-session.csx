#!/usr/bin/env dotnet-script
// .agents/scripts/close-session.csx
// Closes the current session.
// Usage: dotnet-script .agents/scripts/close-session.csx [status]

#load "../lib/ContextDb.csx"

var status = Args.Count > 0 ? Args[0] : "closed";

// Attempt to load session ID from .agents/session.env
var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".agents", "session.env");
if (File.Exists(envFile)) {
    foreach (var line in File.ReadAllLines(envFile)) {
        if (line.StartsWith("MEDIATORLITE_SESSION_ID=")) {
            var sid = line.Split('=')[1].Trim();
            ContextDb.CloseSession(sid, status);
            Console.WriteLine($"Session {sid} closed with status: {status}");
            File.Delete(envFile);
            return;
        }
    }
}

Console.WriteLine("No active session found in .agents/session.env");
