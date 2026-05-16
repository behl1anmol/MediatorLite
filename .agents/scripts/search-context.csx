#!/usr/bin/env dotnet-script
// .agents/scripts/search-context.csx
// Searches recent agent messages, decisions, and mistakes for keywords.
// Usage: dotnet-script .agents/scripts/search-context.csx "keyword"

#load "../lib/ContextDb.csx"

var keyword = Args.Count > 0 ? Args[0] : "";
if (string.IsNullOrWhiteSpace(keyword)) {
    Console.WriteLine("Please provide a search keyword.");
    return;
}

using var conn = new Microsoft.Data.Sqlite.SqliteConnection(ContextDb.ConnectionString);
conn.Open();

// Search decisions
using var cmdDec = conn.CreateCommand();
cmdDec.CommandText = "SELECT topic, choice, rationale FROM decisions WHERE topic LIKE $kw OR rationale LIKE $kw ORDER BY ts DESC LIMIT 5;";
cmdDec.Parameters.AddWithValue("$kw", $"%{keyword}%");
using var rDec = cmdDec.ExecuteReader();
Console.WriteLine($"\n### Decisions containing '{keyword}'");
while (rDec.Read()) Console.WriteLine($"- **{rDec.GetString(0)}**: {rDec.GetString(1)} (Rationale: {rDec.GetString(2)})");

// Search mistakes
using var cmdMis = conn.CreateCommand();
cmdMis.CommandText = "SELECT summary, root_cause, fix FROM mistakes WHERE summary LIKE $kw OR root_cause LIKE $kw ORDER BY ts DESC LIMIT 5;";
cmdMis.Parameters.AddWithValue("$kw", $"%{keyword}%");
using var rMis = cmdMis.ExecuteReader();
Console.WriteLine($"\n### Mistakes containing '{keyword}'");
while (rMis.Read()) Console.WriteLine($"- **{rMis.GetString(0)}** (Fix: {(rMis.IsDBNull(2) ? "none" : rMis.GetString(2))})");

// Search agent_messages
using var cmdMsg = conn.CreateCommand();
cmdMsg.CommandText = "SELECT agent_name, role, content FROM agent_messages WHERE content LIKE $kw ORDER BY ts DESC LIMIT 5;";
cmdMsg.Parameters.AddWithValue("$kw", $"%{keyword}%");
using var rMsg = cmdMsg.ExecuteReader();
Console.WriteLine($"\n### Recent Messages containing '{keyword}'");
while (rMsg.Read()) {
    var content = rMsg.GetString(2);
    Console.WriteLine($"- **{rMsg.GetString(0)}** ({rMsg.GetString(1)}): {content.Substring(0, Math.Min(200, content.Length))}...");
}
