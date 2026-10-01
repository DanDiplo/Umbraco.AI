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
