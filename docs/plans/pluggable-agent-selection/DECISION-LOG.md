# Decision Log

- **01-10-2026** - Profile routing split out of this feature. It picks the model, not the agent,
  and sits at a different layer (`IAIChatClientFactory`). Kept as Part 2 of
  `docs/ideas/pluggable-routing.md`.
- **01-10-2026** - Audience is developers only (code extension point, no backoffice UI). Smallest
  scope that meets the customer/partner ask for their own business rules.
- **01-10-2026** - Stickiness is decided per rule: selection is given the previous pick. The
  default keeps today's re-pick-every-turn behaviour, so nothing changes for existing sites.
- **01-10-2026** - MVP = extension point + LLM classifier as the default + 1-2 simple built-in
  rules, to prove the extension point is enough for real deterministic rules.
- **01-10-2026** - Record the selection reason in the `agent_selected` event and the audit log.
  Cheap to add, and it fixes today's silent fallback to the first agent.
- **01-10-2026** - Ship on v18 first, then backport to v17 (both lines in active support).
- **01-10-2026** - No concrete customer rules available. Selectors get the full context (raw
  request context items, availability context, user groups, conversation), not a curated subset.
- **01-10-2026** - Ordered collection builder (`AIAgentSelectors()`), modelled on guardrail
  resolvers. Rejected single DI-replaceable selector (can't compose) and notifications (no ordering).
- **01-10-2026** - Chain lives in a new `IAIAgentSelectionService`. `SelectAgentForPromptAsync`
  proxies to it, obsolete for v20. Keeps `AIAgentService` from growing.
- **01-10-2026** - Previous pick sent by the browser in AG-UI `forwardedProps.previousAgentId`,
  treated as an untrusted hint. The server stores no conversations on this line. Context items rejected
  (can reach the model's prompt).
- **01-10-2026** - Built-ins: `LLMAgentSelector` (default, unchanged prompt/input) + opt-in
  `StickyAgentSelector`. appsettings rule map and @mention selectors cut.
- **01-10-2026** - Throwing or out-of-candidate selector results are skipped, not fatal. Safe
  because scope filtering runs first.
- **01-10-2026** - Selection reaches the audit log via typed `AIAgentExecutionOptions.Selection`
  -> runtime context `LogKeys` -> `AIAuditLog.Metadata`. No schema change.
- **01-10-2026** - Add `AIAgentSelectedNotification` (observe only) after every `auto` pick.
  Reverses the earlier "no notifications" cut. Rejected a cancelable `Selecting` notification:
  it would be a second way to change the pick, with unclear precedence against selectors.
- **01-10-2026** - Keep orchestration in `IAIAgentSelectionService`, not a method on
  `AIAgentSelectorCollection`. Repo precedent: collection methods are dependency-free lookups or
  loops (`AIEntityAdapterCollection.GetAdapter`, `AIRuntimeContextContributorCollection.Populate`).
  Orchestration that needs other services lives in a service (`AIGuardrailResolutionService` over
  `AIGuardrailResolverCollection`). Selection needs agent lookup, the scope validator, user groups,
  a logger, and `IEventAggregator`.
- **01-10-2026** - Plan: `abortRun()` keeps the previous pick; only `resetConversation()` clears it.
  A cancelled turn is still the same conversation, so sticky shouldn't forget it.
- **01-10-2026** - Plan: the notification publish (T8) is split from the selection service (T7), so the
  chain logic gets reviewed on its own before the side effect is layered on.
- **01-10-2026** - Plan: frontend transport (T2) has no dependencies and runs in group A, because its
  contract is fixed by SPEC.md. It is only proven live in T12, after the backend wire task (T11).
- **01-10-2026** - Plan: v17 backport and the Umbraco.Docs page are post-merge follow-ups, not loop tasks.
- **01-10-2026** - Pending specs are gated with `#if PENDING_AGENT_SELECTION_SPECS` per file (never defined), not `[Fact(Skip=...)]`: the types they use don't exist yet, and Skip can't help code that doesn't compile. Each builder deletes the `#if`/`#endif` in the same commit as the code that makes that file pass. Shared construction lives in `AgentSelectionTestHarness.cs` so signature changes are fixed in one place.
- **01-10-2026** - Build T1: `AIAgentSelectionRequest` docs mention `IAIAgentSelectionService` in plain
  text, not a `<see cref>`, because that type doesn't exist until T7 (it would be a broken-link warning).
- **01-10-2026** - Build: per-task demo-site smoke is skipped for type-only tasks (T1, T3). Their DI
  wiring is proven live in the T11 wire task instead of booting the site for code with no behaviour.
- **01-10-2026** - Build T2: `previousAgentId` is an optional 5th positional parameter on
  `UaiAgentClient.sendMessage` (matching `resume`), not an options bag. The only caller is
  `run.controller.ts`.
- **01-10-2026** - Build T2 note for T12: a persisted Copilot Workspace conversation reopened from
  history has no saved pick, so its first turn sends no `previousAgentId`. Accepted for now.
