# Build Log

- **T1** - `3e6c2c81` - selection types + empty `AIAgentSelectors()` collection registered in
  `AddUmbracoAIAgentCore`. Reviewer PASS on the first round. Reviewer rebuilt independently: unit
  222/222, integration 3/3. Smoke: types only, nothing to drive yet; DI wiring is proven live in T11.
- **T2** - `aa41c24b` - browser sends `forwardedProps.previousAgentId` in auto mode (all 4 send
  sites, resume included), `abortRun()` keeps the pick. Reviewer PASS; one dangling comment fixed
  before commit. Reviewer rebuilt core/agent/agent-ui/copilot: all green. Smoke deferred to T12.
- **T3** - `3daf5c94` - `AIAgentSelectedNotification` type (observe-only, not yet published).
  Reviewer PASS first round, rebuilt independently: unit 222/222, integration 3/3. Smoke: type only.
- **T4** - `b077a7fd` - `LLMAgentSelector` (same prompt/regex/profile, null on failure). Reviewer PASS
  first round; verified the old AG-UI `Content` equals converted `ChatMessage.Text` (plain, multimodal,
  attachment-only). Unit 222/222, integration 3/3. Old copy in `AIAgentService` stays until T9.
- **T5** - `ab4ab57a` - opt-in `StickyAgentSelector`, not registered. Reviewer PASS first round;
  unit 222/222, integration 3/3. Smoke: not registered, proven live in T12.
- **T6** - `70eff88e` - `AIAgentExecutionOptions.Selection` -> SelectorId/SelectionReason in run
  properties + LogKeys. First specs switched on: 7 audit-metadata specs failed first (CS0117), then green.
  Reviewer PASS, traced the full path to `AIAuditMetadata` (no other LogKeys writer). Unit 229/229,
  integration 3/3. Smoke deferred to T11 (no caller sets `Selection` until T10).
- **T7** - `7591f89a` - `IAIAgentSelectionService`: filter, only-candidate, chain (skip non-candidate /
  thrown), fallback. Reviewer FAIL round 1 (returned the selector's agent object, not the candidate; S2
  request specs left gated). Fixed + new spec; re-review PASS. 32 more specs on. Unit 270/270,
  integration 3/3. Smoke deferred to T11 (not called until T9/T10).
- **T8** - `615651b4` - publishes `AIAgentSelectedNotification` once per non-null pick; adds public
  `AIAgentSelectorIds` (human-approved). Reviewer PASS first round, reasoned specs catch missing/duplicate
  publish. 7 more specs on. Unit 277/277, integration 3/3. Smoke deferred to T11.
- **T9** - `afc53851` - `LLMAgentSelector` appended by default; `SelectAgentForPromptAsync` obsolete
  proxy via `StaticServiceProvider`; old classifier code + 3 dead ctor params removed (internal class).
  Reviewer PASS first round, checked same-agent equivalence against `76034955`. Unit 279/279 (x3, no
  flakes), integration 3/3. Smoke deferred to T11 (first task where the live path runs new code).
- **T10** - `a210090e` - controller `auto` branch calls the selection service (previous pick from
  forwardedProps), runs with `Selection`, event gains selectorId/reason; old ctors obsolete. All pending
  gates removed. Reviewer PASS; 4 extra specs added on review (run agent pinned, previousAgentId edge
  cases). Unit 298/298, integration 3/3. Live smoke is T11.
- **T11** - no code commit - live demo site (v18, port 44355) with a `TEMP_` selector + handler:
  (a) custom selector's agent ran; (b) `agent_selected` had `selectorId: temp-legal` + reason; (c) audit
  row Metadata had `Umbraco.AI.Agent.SelectorId`/`SelectionReason`; (d) one `AIAgentSelectedNotification`
  logged; (e) without the TEMP selector the LLM picked (Content Assistant, `selectorId: llm`); (f) default
  collection logged `[LLMAgentSelector]` + extras only, no Sticky. Media Assistant correctly not a
  candidate in the content section (2 candidates). No errors in the site log.
- **T12** - no code commit - sticky opt-in via `TEMP_` composer, driven in Copilot: turn 2 sent
  `forwardedProps.previousAgentId` and kept Content Assistant with `selectorId: sticky` (even for a legal
  question); after Cancel the next turn still sent `previousAgentId`; after switching agent away and back
  to Auto the next request had no `forwardedProps` and the LLM picked Legal Specialist. S5 AC5 (resume
  after tool approval) NOT live-verified: no approval-gated tool in the demo agents; covered by review +
  code (both resume branches pass `previousAgentId`).
- **Follow-up** - `19897541` - selector timeout (OCE without real cancellation) is skipped, not fatal
  (human decision on PR #463). Fail-first spec + AC14 now cancels a real token. Reviewer PASS; unit
  299/299, integration 3/3.
