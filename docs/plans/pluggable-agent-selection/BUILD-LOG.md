# Build Log

- **T1** - `3e6c2c81` - selection types + empty `AIAgentSelectors()` collection registered in
  `AddUmbracoAIAgentCore`. Reviewer PASS on the first round. Reviewer rebuilt independently: unit
  222/222, integration 3/3. Smoke: types only, nothing to drive yet; DI wiring is proven live in T11.
- **T2** - `aa41c24b` - browser sends `forwardedProps.previousAgentId` in auto mode (all 4 send
  sites, resume included), `abortRun()` keeps the pick. Reviewer PASS; one dangling comment fixed
  before commit. Reviewer rebuilt core/agent/agent-ui/copilot: all green. Smoke deferred to T12.
