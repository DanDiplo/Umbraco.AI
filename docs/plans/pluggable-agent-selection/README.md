# Pluggable Agent Selection

Read these in order:

1. [x] **[BRIEF.md](BRIEF.md)** — Developers can't plug their own business rules into Copilot's `auto` agent pick; this makes it a code-only extension point with unchanged default behaviour.
2. [x] **[ARCHITECTURE.md](ARCHITECTURE.md)** — An ordered `AIAgentSelectors()` collection run by a new `IAIAgentSelectionService`, after scope filtering, with an LLM default, opt-in sticky selector, and an `AIAgentSelectedNotification`.
3. [x] **[SPEC.md](SPEC.md)** — No new routes; the `auto` stream reads `forwardedProps.previousAgentId`, adds `selectorId`/`reason` to `agent_selected`, and records both in audit metadata.
4. [x] **[STORIES.md](STORIES.md)** — 6 stories (5 with full acceptance criteria, docs as a placeholder).
5. [x] **[PLAN.md](PLAN.md)** — 12 tasks; the first parallel group is T1 (selection types) and T2 (frontend sends the previous pick).
6. [x] **[BUILD-LOG.md](BUILD-LOG.md)** — 12 tasks done in 10 code commits (T11/T12 are live demo-site checks); 298 unit + 3 integration tests green.

See [DECISION-LOG.md](DECISION-LOG.md) for why things changed along the way.
