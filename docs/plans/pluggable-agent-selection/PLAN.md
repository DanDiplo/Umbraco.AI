# Plan

Task checklist for `umb-build-loop`. Work lands on `v18/dev` (via a worktree branched from it).
v17 backport and the Umbraco.Docs page are follow-ups after merge, not tasks here (see bottom).

Backend lives in `Umbraco.AI.Agent/src/Umbraco.AI.Agent.Core` (new folder
`Agents/Selection/` unless noted) and `Umbraco.AI.Agent.Web`. Specs live in
`Umbraco.AI.Agent/tests/Umbraco.AI.Agent.Tests.Unit`.

- [ ] **T1** - story: S1, S2. Add the selection types in `Agents/Selection/`: `IAIAgentSelector`,
  `AIAgentSelectionRequest`, `AIAgentSelectionResult`, `AIAgentSelectionInput`,
  `AIAgentSelectorCollection`, `AIAgentSelectorCollectionBuilder` (ordered), and
  `builder.AIAgentSelectors()` in a new `Configuration/UmbracoBuilderExtensions.Selectors.cs`
  (same shape as `UmbracoBuilderExtensions.Surfaces.cs`). Register the empty collection in the
  agent composer setup. Types only, no behaviour.
  depends-on: none. parallel-group: A

- [ ] **T2** - story: S5 (AC3-AC5), frontend. In `Umbraco.AI.Agent.UI` `run.controller.ts`: when
  the agent is `auto` and `resolvedAgent$` has a value, send `forwardedProps.previousAgentId`. Store
  `selectorId`/`reason` from `agent_selected`. `resetConversation()` clears the pick, and
  `abortRun()` **keeps** it (resolves the ARCHITECTURE.md TODO). In `uai-agent-client.ts`, merge
  `previousAgentId` into `forwardedProps` next to `resume`. Confirm `uai-http-agent.ts` strips only
  `resume`. Widen the `resolvedAgent$` type in `chat/context.ts` with optional
  `selectorId`/`reason`. Acceptance: `npm run build:agent` and `npm run build:agent-ui` are green.
  Behaviour is proven in T12.
  depends-on: none. parallel-group: A

- [ ] **T3** - story: S4. Add `AIAgentSelectedNotification` (`StatefulNotification`, not
  cancelable) in `Agents/` next to `AIAgentExecutedNotification`, with `Selection`, `Request` and
  `Messages`. The XML docs must say "selected does not mean ran". Type only, not published yet.
  depends-on: T1. parallel-group: B

- [ ] **T4** - story: S1 (AC5, AC6, AC16, AC17). Add `LLMAgentSelector` (`SelectorId = "llm"`).
  Move `BuildClassificationPrompt` and `ParseAgentIdFromResponse` out of `AIAgentService`
  unchanged. Same classifier profile and same prompt, using only the last user message text.
  Return `null` (never the first agent) on no profile, an unparseable reply, or a non-candidate
  GUID.
  depends-on: T1. parallel-group: B

- [ ] **T5** - story: S5 (AC2, AC8). Add `StickyAgentSelector` (`SelectorId = "sticky"`). It
  returns `PreviousAgent` or `null`. Do **not** register it.
  depends-on: T1. parallel-group: B

- [ ] **T6** - story: S3 (AC4, AC5, AC7, AC8). Add `AIAgentExecutionOptions.Selection`
  (`AIAgentSelectionResult?`). In `AIAgentService.StreamAgentAGUIAsync` (options overload),
  when `Selection` is set, add `Umbraco.AI.Agent.SelectorId` (and `SelectionReason` when not null)
  to the run's additional properties and its `LogKeys` array, alongside `RunId`/`ThreadId`. Add
  the two context-key constants to `Constants.ContextKeys`.
  depends-on: T1. parallel-group: B

- [ ] **T7** - story: S1 (AC1-AC4, AC7, AC8, AC11-AC15), S2 (all), S5 (AC1, AC6). Add
  `IAIAgentSelectionService` + internal `AIAgentSelectionService`: surface lookup, active + scope
  filter (moved from `SelectAgentForPromptAsync`), 0 -> null, 1 -> `only-candidate`, user groups
  resolved once, `PreviousAgent` resolved against the candidates, the selector chain (null ->
  next; non-candidate -> warn and skip; throw -> log and skip; cancellation propagates), and
  `fallback`. No notification yet.
  depends-on: T1. parallel-group: B

- [ ] **T8** - story: S4 (AC1-AC6). Publish `AIAgentSelectedNotification` from
  `AIAgentSelectionService` via `IEventAggregator.PublishAsync`, for every non-null outcome, with
  the same `AIAgentSelectionRequest` the selectors saw. Build a request for `only-candidate` too.
  depends-on: T3, T7. parallel-group: C

- [ ] **T9** - story: S1 (AC9, AC10), S5 (AC9). Registration and compatibility: append
  `LLMAgentSelector` to the default collection, register `IAIAgentSelectionService`, and make
  `AIAgentService.SelectAgentForPromptAsync` build an input and proxy to the new service, marked
  `[Obsolete("Use IAIAgentSelectionService.SelectAgentAsync. Will be removed in v20")]`. Remove the
  now-dead classifier code from `AIAgentService`.
  depends-on: T4, T7. parallel-group: C

- [ ] **T13** - story: S7 (AC6-AC8, AC11). Add `IAIAgentResolutionService` + internal
  `AIAgentResolutionService` with `AIAgentResolutionInput`, `AIAgentResolutionResult`,
  `AIAgentResolutionFailure` and `AIUnavailableAgentBehavior` (ARCHITECTURE.md, Shared agent
  resolution). Explicit: resolve by GUID or alias, active check, scope check when `SurfaceId` is
  set, then `Fail` or `UseAuto`. Auto: `SurfaceRequired` when there's no surface, otherwise call
  `IAIAgentSelectionService` (`NoCandidates` on null). Register it in the composer.
  depends-on: T7. parallel-group: C

- [ ] **T14** - story: S3, S7 (AC2). Move `PrependAgentSelectedEvent` out of
  `StreamAgentAGUIController` into a public static `AGUIAgentSelectionEvents.Prepend(stream,
  selection)` in `Agent.Core/AGUI/`. It builds `agent_selected` with `agentId`, `agentName`,
  `agentAlias`, `selectorId` and `reason`. Unit-test the event shape.
  depends-on: T1. parallel-group: B

- [ ] **T10** - story: S1 (AC18-AC20), S3 (AC1-AC3, AC6, AC9), S4 (AC7), S5 (AC7, AC10), S7
  (AC11). In `StreamAgentAGUIController`, resolve the agent (both `auto` and explicit) through
  `IAIAgentResolutionService` with `UnavailableAgentBehavior = Fail`, and `previousAgentId` read
  from `forwardedProps` (bad or missing values become null, never an error). Map each `Failure`
  to today's 400/404 `ProblemDetails`, word-for-word. When `Selection` is set, call the options
  overload with `new AIAgentExecutionOptions { Selection = result.Selection }` and prepend the
  event with `AGUIAgentSelectionEvents.Prepend`. Explicit-agent behaviour stays as today,
  including the surface-scope 404. Extend `StreamAgentAGUIControllerScopeTests` or add a
  sibling test class.
  depends-on: T6, T8, T9, T13, T14. parallel-group: D

- [ ] **T15** - story: S7 (AC1-AC5, AC7-AC10). In `StreamConversationAGUIController`, replace
  `ResolveAgentIdAsync` with `IAIAgentResolutionService`:
  - Input: `RequestedAgentIdOrAlias = conversation.AgentIdOrAlias`, surface `copilot-workspace`,
    `UnavailableAgentBehavior = UseAuto`, and messages = persisted history + inbound (convert
    with the existing AG-UI message converter). `previousAgentId` comes from `forwardedProps`.
  - Map `NoCandidates` to today's "No agent available" 404.
  - When `Selection` is set, merge it into the existing options
    (`ConversationHistory`, `AdditionalProperties`, `Selection`) and prepend the event.
  - Drop the now-unused `GetLastUserMessageTextAsync` call if nothing else uses it.
  Tests in the Workspace unit project.
  depends-on: T6, T8, T13, T14. parallel-group: D

- [ ] **T11** - **wire: backend selection into the demo site.** Add a throwaway `TEMP_` selector
  plus composer in `demos/v18/Umbraco.AI.DemoSite/` (gitignored), with two or more Copilot agents.
  Through a real `auto` request: (a) the custom selector's agent runs; (b) the `agent_selected`
  event carries its `selectorId`/`reason`; (c) the agent run's audit log entry has the
  `SelectorId`/`SelectionReason` metadata; (d) a `TEMP_` notification handler logs one
  `AIAgentSelectedNotification`; (e) with the TEMP selector removed, the LLM selector still picks
  (or `fallback` shows when no classifier profile exists); (f) the resolved
  `AIAgentSelectorCollection` holds `LLMAgentSelector` and not `StickyAgentSelector` (S5 AC9). (f)
  is checked here because the unit project can't run the full composer (no TypeLoader outside a
  CMS host).
  depends-on: T10. parallel-group: E

- [ ] **T16** - **wire: Workspace auto selection in the demo site.** With the T11 `TEMP_`
  selector still registered, open a Copilot Workspace conversation on Auto and verify:
  (a) the custom selector's agent runs; (b) the network panel shows `agent_selected` as the first
  event with `selectorId`/`reason`, and the chat shows which agent answered; (c) the audit entry
  has `SelectorId`; (d) a second turn sends `forwardedProps.previousAgentId`; (e) setting the
  conversation's agent to one not opted in to Workspace makes Auto decide instead (S7 AC7).
  depends-on: T2, T11, T15. parallel-group: F

- [ ] **T12** - **wire: sticky selection end to end in Copilot.** With `StickyAgentSelector`
  registered by a `TEMP_` composer in the demo site, verify in the browser (network panel):
  S5 AC3 (2nd turn sends `previousAgentId` and keeps agent A, `selectorId: "sticky"`), AC4 (a new
  conversation sends none), AC5 (a tool-approval resume still carries the `resume` entries), and
  that after cancelling a run the next turn still sends `previousAgentId`.
  depends-on: T2, T11. parallel-group: F

## Follow-ups (after merge, not `umb-build-loop` tasks)

- **v17 backport** through the `backport` skill, as a draft PR into `v17/dev`.
- **S6 docs:** an Umbraco.Docs "Extending > Agent selection" page for v17 and v18 (selectors,
  sticky opt-in, the notification, audit metadata keys, and that both Copilot and Copilot
  Workspace use them).
- **Server-side previous pick for Workspace** (`LastAgentId` on the conversation, needs a
  migration). See ARCHITECTURE.md TODO.
- **v17 note:** v17/dev also has Copilot Workspace and the same `StreamConversationAGUIController`,
  so T13-T16 port too.
