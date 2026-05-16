# Workflow: Debug Environment

Dumps the current environment variables to help discover Antigravity's native conversation ID.

## Steps

1. Run: `printenv | sort`
2. Look for variables containing `CONVERSATION`, `ANTIGRAVITY`, `GEMINI`, or `SESSION`.
3. Report any findings to the user so it can be used for session correlation in `/start-session`.
