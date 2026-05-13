#!/usr/bin/env dotnet-script
// .claude/hooks/05-inject-context.csx
// Event: beforeTurn (Cursor) / PreToolUse throttled (Claude Code)
// Purpose: give cross-chat persistence — prints the most recent N agent_messages
// for the current session so the agent can fold them into its context.
//
// In Claude Code, this hook is called by pre-tool-use.sh at most once every 60 seconds
// to approximate Cursor's "once per user turn" semantics. The stdout from a PreToolUse
// hook is injected into Claude's context as a system-level message.

#load "../lib/ContextDb.csx"

using System;
using System.Text;

try
{
    var sid = Environment.GetEnvironmentVariable("MEDIATORLITE_SESSION_ID");

    // Fallback: read the session file for Claude Code, where each hook invocation
    // is a separate process and env vars set by 01-bootstrap.csx do not survive.
    if (string.IsNullOrWhiteSpace(sid))
    {
        var sessionFile = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(ContextDb.DbPath)!, ".current-session");
        if (System.IO.File.Exists(sessionFile))
            sid = System.IO.File.ReadAllText(sessionFile).Trim();
    }

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
