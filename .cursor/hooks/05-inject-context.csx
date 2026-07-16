#!/usr/bin/env dotnet-script
// .cursor/hooks/05-inject-context.csx
// Event: beforeTurn (before each user turn)
// Purpose: give cross-chat persistence — prints the most recent N agent_messages
// for the current session so Cursor can fold them into the context.
//
// The hook's stdout is surfaced to the agent via Cursor's beforeTurn contract.
// We emit a compact markdown block preceded by a sentinel so the agent recognises it.

#load "../lib/ContextDb.csx"

using System;
using System.Text;

try
{
    var sid = Environment.GetEnvironmentVariable("MEDIATORLITE_SESSION_ID");
    if (string.IsNullOrWhiteSpace(sid)) { return; }

    var msgs = ContextDb.ReadRecent(limit: 20, sessionId: sid);
    if (msgs.Count == 0) { return; }

    var sb = new StringBuilder();
    sb.AppendLine("<!-- MEDIATORLITE_PRIOR_CONTEXT -->");
    sb.AppendLine("## Prior agent context (last 20 messages this session)");
    foreach (var m in msgs)
    {
        var content = m.Content.Length > 400 ? m.Content.Substring(0, 400) + "…" : m.Content;
        sb.AppendLine($"- `{m.Ts}` **{m.Agent}** ({m.Role}{(m.Target is null ? "" : " → " + m.Target)}): {content}");
    }
    sb.AppendLine("<!-- /MEDIATORLITE_PRIOR_CONTEXT -->");
    Console.Write(sb.ToString());
    ContextDb.LogHookEvent("05-inject-context.csx", "beforeTurn", "ok", 0, new { count = msgs.Count });
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[hook:05-inject-context] warn: {ex.Message}");
}
