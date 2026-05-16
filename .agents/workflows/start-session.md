# Workflow: Start Session

Initialize a new MediatorLite agent session in the SQLite DB and record the session ID.

## Steps

1. Determine Session ID:
   Run `/debug-env` implicitly to see if a conversation ID exists.
   If Antigravity provides a distinct conversation ID in the environment, use it.
   Otherwise, generate a distinct UUID as a fallback to use as the session ID.

2. Get the current git branch:
   Run: `git branch --show-current`
   Save result as BRANCH.

3. Create a new session in the DB and write the session env file:
   Run: `dotnet-script .agents/scripts/start-session.csx "$BRANCH" "Auto-started session" "$SESSION_ID"`
   This writes `.agents/session.env` with the new session ID.
