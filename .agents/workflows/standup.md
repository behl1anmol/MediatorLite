# Workflow: Standup

Queries the sprint backlog to report current status.

## Steps

1. Run the query:
   Run: `sqlite3 .cursor/db/session.sqlite "SELECT id, status, assigned_agent, priority, story FROM sprint_backlog WHERE status != 'done' ORDER BY priority, id LIMIT 15;"`

2. Format the output into a clear, readable Markdown table.

3. Ask the user which item to work on next.
