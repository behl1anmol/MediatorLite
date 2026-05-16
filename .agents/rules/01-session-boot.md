---
activation: always
---
# Session Boot & Dynamic Context

1. **Auto-Start Session:** At the beginning of any work, check if `.agents/session.env` exists. If it does not, immediately run the `/start-session` workflow.
2. **Dynamic Context Querying:** DO NOT dump the entire session history into context. Instead, before researching or writing code for a task, query the database for existing context, decisions, or mistakes related to your task using:
   `dotnet-script .agents/scripts/search-context.csx "<keywords>"`
   *Always query the database first before doing independent research, as previous context might contain the answers.*
