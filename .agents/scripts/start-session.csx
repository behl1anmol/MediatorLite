#!/usr/bin/env dotnet-script
// .agents/scripts/start-session.csx
// Creates a new session row and writes .agents/session.env
// Usage: dotnet-script .agents/scripts/start-session.csx [branch] [user-request] [session-id-override]

#load "../lib/ContextDb.csx"

var branch = Args.Count > 0 ? Args[0] : null;
var userRequest = Args.Count > 1 ? string.Join(" ", Args.Skip(1)) : null;
var sidOverride = Args.Count > 2 ? Args[2] : null;

if (!string.IsNullOrWhiteSpace(sidOverride)) {
    Environment.SetEnvironmentVariable("MEDIATORLITE_SESSION_ID", sidOverride);
}

var sid = ContextDb.EnsureSession(userRequest, branch);

// Write the session.env file for Antigravity to pick up
var envPath = Path.Combine(FindAgentsDir(), "session.env");
File.WriteAllText(envPath, $"MEDIATORLITE_SESSION_ID={sid}\n");

Console.WriteLine($"Session started: {sid}");
Console.WriteLine($"Written to: {envPath}");

static string FindAgentsDir()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var agents = Path.Combine(dir.FullName, ".agents");
        if (Directory.Exists(agents)) return agents;
        if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return Path.Combine(dir.FullName, ".agents");
        dir = dir.Parent;
    }
    return ".agents";
}
