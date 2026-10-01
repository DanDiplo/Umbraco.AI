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
