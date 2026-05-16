---
activation: always
---
# Agentic Handoff Contract

Every Antigravity turn that touches `src/` or `tests/` MUST:

1. Read the current session ID from `.agents/session.env` at turn start (or query if already loaded).
2. Query relevant prior context with `.agents/scripts/search-context.csx` if needed.
3. Log significant decisions: `dotnet-script .agents/scripts/log-decision.csx "<topic>" "<choice>" "<rationale>" "<agent>"`
4. Before fixing build/test errors on your own code, log the mistake: `dotnet-script .agents/scripts/log-mistake.csx "<agent>" "<category>" "<summary>"` (IT IS FORBIDDEN TO FIX UNTIL THIS IS LOGGED).
5. At the end of every successful turn, emit this literal block in your message:

   LessonsSuggested: <title>: <why>  OR  none
   MemoriesSuggested: <title>: <why>  OR  none
   ReasoningSummary: <rationale>

6. After any code change, log handoff: referenced in session context.
