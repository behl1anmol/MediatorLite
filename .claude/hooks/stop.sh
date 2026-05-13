#!/usr/bin/env bash
# .claude/hooks/stop.sh
# Claude Code Stop event dispatcher.
#
# Fires when Claude finishes generating a response (end of turn).
#
# Maps Cursor hook events:
#   beforeCompaction -> 00-save-context.csx  (snapshot DB to JSON before context is trimmed)
#   onSessionEnd     -> 99-close-session.csx  (mark session closed, VACUUM)
#
# Note: Claude Code's Stop fires after every response, whereas Cursor's beforeCompaction only
# fires before a compaction event. Running save-context on every Stop is safe because
# the snapshot is timestamped and idempotent — it just produces extra snapshot files.
# The 99-close-session hook only truly closes the session when the user's request is
# fully satisfied (it reads the session status before deciding to close).

HOOKS_DIR="$(cd "$(dirname "$0")" && pwd)"

bash "$HOOKS_DIR/run-hook.sh" 00-save-context.csx
bash "$HOOKS_DIR/run-hook.sh" 99-close-session.csx

exit 0
