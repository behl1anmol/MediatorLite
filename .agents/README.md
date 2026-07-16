# Antigravity `.agents/` Workflow

This directory contains the rules, workflows, skills, and scripts necessary for the Antigravity multi-agent system.
It coexists with the `.cursor/` folder to maintain dual-toolchain compatibility.

## Setup

Point git at the hooks in this directory (once per clone — `core.hooksPath` is local config and is
never cloned). Without this, `.agents/hooks/pre-commit` and `pre-push` never run and `git commit` /
`git push` silently bypass the build, format, lint, and test gates:

```bash
git config core.hooksPath .agents/hooks
```

Then run the following workflow in Antigravity to verify your environment:

```text
/check-setup
```

## Workflows

You can manually trigger these via the Antigravity input:
- `/start-session` — Initializes a new session DB context.
- `/bug-fix` — Runs the TDD-ordered bug fix workflow.
- `/standup` — Queries the sprint backlog.
- `/code-review` — Runs an automated code review on the current diff.

## Windows Users

The git hooks in `.agents/hooks/` are Bash scripts. If you are on Windows, ensure you use **Git Bash** or WSL so these scripts execute correctly during `git commit` and `git push`.
