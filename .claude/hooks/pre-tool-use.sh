#!/usr/bin/env bash
# .claude/hooks/pre-tool-use.sh
# Claude Code PreToolUse event dispatcher.
#
# Receives the JSON hook payload on stdin from Claude Code:
#   { "session_id": "...", "tool_name": "Bash", "tool_input": { "command": "git commit ..." }, ... }
#
# Maps Cursor hook events:
#   onSessionStart  -> 01-bootstrap.csx  (idempotent; only creates a new session when none exists)
#   beforeTurn      -> 05-inject-context.csx  (throttled to ~once per 60s to avoid flooding context)
#   beforeCommit    -> 20-autoreview.csx, 21-build-gate.csx, 22-lint-gate.csx  (git commit pattern)
#   beforePush      -> 30-test-gate.csx  (git push pattern)
#
# Exit codes:
#   0  allow the tool call
#   2  block the tool call (Claude Code interprets this as a hard block; stderr is shown to user)

HOOKS_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HOOKS_DIR/../.." && pwd)"

# Read the full JSON payload from stdin.
PAYLOAD="$(cat)"

# Parse tool name and (for Bash) the command string.
# python3 is the most portable JSON parser available without extra installs.
TOOL_NAME="$(echo "$PAYLOAD" | python3 -c \
    "import sys,json; print(json.loads(sys.stdin.read()).get('tool_name',''))" 2>/dev/null || echo "")"

BASH_CMD=""
if [ "$TOOL_NAME" = "Bash" ]; then
    BASH_CMD="$(echo "$PAYLOAD" | python3 -c \
        "import sys,json; d=json.loads(sys.stdin.read()); print(d.get('tool_input',{}).get('command',''))" \
        2>/dev/null || echo "")"
fi

# ── 1. Bootstrap (onSessionStart equivalent) ─────────────────────────────────
# Only runs dotnet-script when no active session file exists; otherwise fast path.
SESSION_FILE="$REPO_ROOT/.claude/db/.current-session"
if [ ! -f "$SESSION_FILE" ] || [ ! -s "$SESSION_FILE" ]; then
    bash "$HOOKS_DIR/run-hook.sh" 01-bootstrap.csx
fi

# ── 2. Context injection (beforeTurn equivalent, throttled) ──────────────────
# Inject prior agent_messages into Claude's context at most once every 60 seconds.
# This approximates Cursor's "beforeTurn" (once per user message) without a dedicated event.
LAST_INJECT_FILE="$REPO_ROOT/.claude/db/.last-inject"
NOW="$(date +%s 2>/dev/null || echo 0)"
LAST="$(cat "$LAST_INJECT_FILE" 2>/dev/null || echo 0)"
AGE="$(( NOW - LAST ))"
if [ "$AGE" -gt 60 ]; then
    bash "$HOOKS_DIR/run-hook.sh" 05-inject-context.csx
    echo "$NOW" > "$LAST_INJECT_FILE"
fi

# ── 3. git commit gate (beforeCommit equivalent) ─────────────────────────────
if echo "$BASH_CMD" | grep -qE '^\s*git\s+commit'; then
    bash "$HOOKS_DIR/run-hook.sh" 20-autoreview.csx || exit 2
    bash "$HOOKS_DIR/run-hook.sh" 21-build-gate.csx || exit 2
    bash "$HOOKS_DIR/run-hook.sh" 22-lint-gate.csx  || exit 2
fi

# ── 4. git push gate (beforePush equivalent) ─────────────────────────────────
if echo "$BASH_CMD" | grep -qE '^\s*git\s+push'; then
    bash "$HOOKS_DIR/run-hook.sh" 30-test-gate.csx || exit 2
fi

exit 0
