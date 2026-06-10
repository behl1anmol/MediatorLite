#!/usr/bin/env dotnet-script
// .cursor/hooks/99-close-session.csx
// Event: onSessionEnd
// Purpose: mark the session row closed, force one final snapshot, VACUUM.

#load "../lib/ContextDb.csx"
#load "./00-save-context.csx"   // re-use the compaction dumper for a parting snapshot

using System;

try
{
    var sid = Environment.GetEnvironmentVariable("MEDIATORLITE_SESSION_ID");
    if (!string.IsNullOrWhiteSpace(sid) && ContextDb.SessionExists(sid!))
    {
        ContextDb.CloseSession(sid!, status: "closed");
        Console.WriteLine($"[hook:99-close-session] closed {sid}");
    }
    ContextDb.LogHookEvent("99-close-session.csx", "onSessionEnd", "ok", 0, new { sid });
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[hook:99-close-session] warn: {ex.Message}");
}
