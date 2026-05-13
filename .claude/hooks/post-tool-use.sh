#!/usr/bin/env bash
# .claude/hooks/post-tool-use.sh
# Claude Code PostToolUse event dispatcher.
#
# Receives the JSON hook payload on stdin from Claude Code:
#   {
#     "session_id": "...", "tool_name": "ExitPlanMode",
#     "tool_input": {...},
#     "tool_response": { "stdout": "...", "stderr": "...", "isError": false }
#   }
#
# Maps Cursor hook events:
#   afterPlanCreation -> 40-snapshot-plan.csx  (fired after ExitPlanMode or TodoWrite)
#   onAgentError      -> 10-log-mistake.csx    (fired when tool_response.isError == true)

HOOKS_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HOOKS_DIR/../.." && pwd)"

PAYLOAD="$(cat)"

TOOL_NAME="$(echo "$PAYLOAD" | python3 -c \
    "import sys,json; print(json.loads(sys.stdin.read()).get('tool_name',''))" 2>/dev/null || echo "")"

IS_ERROR="$(echo "$PAYLOAD" | python3 -c \
    "import sys,json; d=json.loads(sys.stdin.read()); print(d.get('tool_response',{}).get('isError',False))" \
    2>/dev/null || echo "False")"

# ── 1. Plan snapshot (afterPlanCreation equivalent) ──────────────────────────
# ExitPlanMode is the Claude Code analogue of Cursor's CreatePlan tool.
# TodoWrite is also checked because agents may write plan files via todo updates.
if [ "$TOOL_NAME" = "ExitPlanMode" ] || [ "$TOOL_NAME" = "TodoWrite" ]; then
    bash "$HOOKS_DIR/run-hook.sh" 40-snapshot-plan.csx
fi

# ── 2. Error logging (onAgentError equivalent) ───────────────────────────────
if [ "$IS_ERROR" = "True" ]; then
    MISTAKE_PAYLOAD="$(echo "$PAYLOAD" | python3 -c "
import sys, json
d = json.loads(sys.stdin.read())
resp = d.get('tool_response', {})
summary = (resp.get('stderr') or resp.get('stdout') or 'Tool error')[:2000]
print(json.dumps({
    'agent':    d.get('tool_name', 'unknown'),
    'category': 'other',
    'summary':  summary,
    'error':    summary
}))
" 2>/dev/null || echo '{"agent":"unknown","category":"other","summary":"Tool error"}')"
    echo "$MISTAKE_PAYLOAD" | bash "$HOOKS_DIR/run-hook.sh" 10-log-mistake.csx
fi

exit 0
