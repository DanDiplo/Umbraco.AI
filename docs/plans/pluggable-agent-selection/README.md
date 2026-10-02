# Pluggable Agent Selection

Read these in order:

1. [x] **[BRIEF.md](BRIEF.md)** — Developers can't plug their own business rules into Copilot's `auto` agent pick; this makes it a code-only extension point with unchanged default behaviour.
2. [x] **[ARCHITECTURE.md](ARCHITECTURE.md)** — An ordered `AIAgentSelectors()` collection run by a new `IAIAgentSelectionService`, after scope filtering, with an LLM default, opt-in sticky selector, and an `AIAgentSelectedNotification`.
3. [x] **[SPEC.md](SPEC.md)** — No new routes; the Copilot `auto` stream and the Copilot Workspace conversation stream both resolve through `IAIAgentResolutionService`, read `forwardedProps.previousAgentId`, send `agent_selected` with `selectorId`/`reason`, and record both in audit metadata.
4. [x] **[STORIES.md](STORIES.md)** — 7 stories (6 with full acceptance criteria, docs as a placeholder). S7 covers Copilot Workspace.
5. [x] **[PLAN.md](PLAN.md)** — 16 tasks; the first parallel group is T1 (selection types) and T2 (frontend sends the previous pick). T13-T16 add the shared resolution service and move Workspace onto it.
6. [ ] **[BUILD-LOG.md](BUILD-LOG.md)** — <pending>

See [DECISION-LOG.md](DECISION-LOG.md) for why things changed along the way.
