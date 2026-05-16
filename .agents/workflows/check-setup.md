# Workflow: Check Setup

Verifies and installs required tools for the agentic workflow.

## Steps

1. Check if `dotnet-script` is installed:
   Run: `dotnet script --version`
   If it fails, install it: `dotnet tool install -g dotnet-script`

2. Check if git hooks are configured:
   Run: `git config core.hooksPath`
   If it does not output `.agents/hooks`, configure it: `git config core.hooksPath .agents/hooks`

3. Ensure hook scripts are executable:
   Run: `chmod +x .agents/hooks/pre-commit .agents/hooks/pre-push`

4. Ensure `.agents/session.env` is in the root `.gitignore`.
