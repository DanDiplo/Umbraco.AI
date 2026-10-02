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
- **02-10-2026** - Scope widened to Copilot Workspace's conversation stream. It had its own copy of
  agent resolution that had already drifted (no `agent_selected` event, no surface check on the
  stored agent). A Copilot-only extension point would have left Workspace on the old classifier.
- **02-10-2026** - New `IAIAgentResolutionService` above `IAIAgentSelectionService` owns
  explicit-or-auto resolution for every endpoint. Selection stays a pure chain. Rejected: each
  controller calling the selection service itself, which keeps the duplicated explicit-agent
  logic where the drift happened.
- **02-10-2026** - Unavailable explicit agents are a per-caller policy (`Fail` for Copilot, `UseAuto`
  for Workspace), keeping both endpoints' current behaviour. The surface-scope check applies to
  both. Workspace skipping it was the drift, not a product choice.
- **02-10-2026** - `agent_selected` is built by one shared helper in `Agent.Core/AGUI`.
  `Copilot.Workspace.Web` does not reference `Agent.Web`.
- **02-10-2026** - Workspace selectors get persisted history + inbound messages, to keep S2's
  "full conversation" promise. That costs one extra message load per auto turn; accepted, with
  sharing the load as a TODO.
- **02-10-2026** - Corrects the 01-10 note "the server stores no conversations on this line".
  Copilot Workspace does persist them. The browser hint stays the shared mechanism for this
  feature, because plain Copilot still has nothing server-side. Workspace server-side storage
  (`LastAgentId`) is a follow-up.
- **01-10-2026** - Pending specs are gated with `#if PENDING_AGENT_SELECTION_SPECS` per file (never defined), not `[Fact(Skip=...)]`: the types they use don't exist yet, and Skip can't help code that doesn't compile. Each builder deletes the `#if`/`#endif` in the same commit as the code that makes that file pass. Shared construction lives in `AgentSelectionTestHarness.cs` so signature changes are fixed in one place.
