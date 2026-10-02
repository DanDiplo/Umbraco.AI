# Spec

## Management API surface

No new routes or DTOs. Two existing endpoints change: Copilot's when the agent is `auto`, and
Copilot Workspace's conversation stream (see the second section). Both resolve their agent through
`IAIAgentResolutionService`.

### `POST .../{agentIdOrAlias}/stream-agui` with `auto` (`StreamAgentAGUIController.StreamAgentAGUI`)

**Request** - unchanged shape (`AGUIRunRequest`). New optional read:

```json
{
  "forwardedProps": { "previousAgentId": "<guid>" }
}
```

Guarantees:

1. **No custom selectors registered, no `previousAgentId`:** the selected agent is the same one
   today's code picks for the same request.
2. **Scope is never bypassed.** The selected agent is always active and passes
   `AIAgentScopeValidator.IsAgentAvailable` for the request's surface and context, whatever any
   selector returns.
3. **Order.** Selectors run in `AIAgentSelectorCollection` order. The first non-null result that
   is a candidate wins. Later selectors don't run.
4. **Invalid selector result.** A selector returning an agent that isn't a candidate is ignored
   (warning logged), and the next selector runs.
5. **Throwing selector.** A selector that throws (other than `OperationCanceledException`) is
   skipped (error logged), and the next selector runs.
6. **Single candidate.** No selector runs. `selectorId` is `"only-candidate"`.
7. **Nobody decides.** The first candidate is used. `selectorId` is `"fallback"`.
8. **`previousAgentId`** is given to selectors as `PreviousAgent` only if it parses as a GUID and
   matches a candidate. Otherwise `PreviousAgent` is `null`. It never causes an error response.
9. **Selector input.** Selectors receive the full converted conversation (all messages, including
   attachments), all request context items, the availability context, the surface ID, the
   current user's group IDs, and the frontend tools.
10. **Unchanged errors.** No surface -> 400. No candidates -> 404. Same titles and details as
    today.
11. **Non-`auto` agents** behave exactly as today and ignore `previousAgentId`.
12. **Notification.** Every `auto` pick publishes exactly one `AIAgentSelectedNotification`,
    before the agent run starts, whose `Selection` matches the `agent_selected` event. That
    includes `only-candidate` and `fallback`. None is published when there are no candidates
    (404) or for non-`auto` agents.

**Response** - the first event stays the `agent_selected` custom event, with two added fields:

```json
{
  "name": "agent_selected",
  "value": {
    "agentId": "<guid>",
    "agentName": "...",
    "agentAlias": "...",
    "selectorId": "llm | sticky | only-candidate | fallback | <custom>",
    "reason": "<string or null>"
  }
}
```

**Audit log** - the agent run's audit entry (`AIAuditLog.Metadata`) contains
`Umbraco.AI.Agent.SelectorId` and, when non-null, `Umbraco.AI.Agent.SelectionReason`. They are
absent for non-`auto` runs.

### C# extension surface (public, `Umbraco.AI.Agent.Core`)

- `IAIAgentSelector`, `AIAgentSelectionRequest`, `AIAgentSelectionResult`
- `AIAgentSelectorCollection`, `AIAgentSelectorCollectionBuilder`, `builder.AIAgentSelectors()`
- `IAIAgentSelectionService.SelectAgentAsync`, `AIAgentSelectionInput`
- `LLMAgentSelector` (registered), `StickyAgentSelector` (not registered)
- `AIAgentExecutionOptions.Selection` (new optional property)
- `AIAgentSelectedNotification` (`StatefulNotification`, not cancelable): `Selection`,
  `Request`, `Messages`
- `IAIAgentService.SelectAgentForPromptAsync` - still works, returns the same agent the new service
  selects, marked `[Obsolete("Use IAIAgentSelectionService.SelectAgentAsync. Will be removed in v20")]`

**`StickyAgentSelector`:** returns `PreviousAgent` with `SelectorId = "sticky"` when it is set,
otherwise `null`.

**`LLMAgentSelector`:** same classifier prompt and input as today (last user message text). On no
classifier profile, an unparseable reply, or a GUID that isn't a candidate, it returns `null`
instead of picking the first agent.

### `POST .../conversations/{id}/stream-agui` (`StreamConversationAGUIController.StreamAgentAGUI`)

**Request** - unchanged shape. Reads `forwardedProps.previousAgentId` the same way as above.

Guarantees:

13. **Same chain.** When the conversation's agent is null or `auto`, the agent is chosen by the
    same selector chain as Copilot, with guarantees 1-9 and 12, for surface `copilot-workspace`.
14. **Full conversation.** Selectors receive the persisted history plus the inbound messages. A
    regenerate (no inbound user message) still gives `LLMAgentSelector` the last persisted user
    message, as today.
15. **Stored agent, scope enforced.** A stored explicit agent runs only if it is active **and**
    available on the `copilot-workspace` surface. Otherwise the conversation falls back to auto
    selection, as it does today for a missing or inactive agent.
16. **Event.** An auto pick prepends the same `agent_selected` event as Copilot, including
    `selectorId` and `reason`. Explicit agents send none, same as Copilot.
17. **Audit.** Auto runs carry the same `SelectorId` / `SelectionReason` audit metadata.
18. **Errors.** Conversation not found -> 404, unchanged. No candidates -> 404 "No agent
    available", unchanged wording.

### C# extension surface: agent resolution (public, `Umbraco.AI.Agent.Core`)

- `IAIAgentResolutionService.ResolveAgentAsync`, `AIAgentResolutionInput`,
  `AIAgentResolutionResult`, `AIAgentResolutionFailure`, `AIUnavailableAgentBehavior`
- `AGUIAgentSelectionEvents.Prepend(stream, selection)` - builds and prepends `agent_selected`

## Frontend components

All in `Umbraco.AI.Agent.UI` (chat library) plus its transport in
`Umbraco.AI.Agent.Web.StaticAssets`. No new components, no visual changes.

- **`run.controller.ts`** - when the current agent is `auto` and `resolvedAgent$` holds a value,
  each run sends `forwardedProps.previousAgentId = resolvedAgent.agentId`. On `agent_selected`,
  it stores `selectorId` and `reason` along with the existing fields. `resetConversation()` clears
  the previous pick, so a new chat starts fresh. (`abortRun()` behaviour is a TODO, see
  ARCHITECTURE.md.)
- **`uai-agent-client.ts`** - merges `previousAgentId` into `forwardedProps` next to the existing
  `resume` entries. Must not drop either one.
- **`uai-http-agent.ts`** - still strips `resume` only. `previousAgentId` passes through to the
  server unchanged.
- **`resolvedAgent$` type** (`chat/context.ts`) - gains optional `selectorId?: string` and
  `reason?: string | null`. Optional, so existing consumers (Copilot) still compile.
- **Explicit (non-`auto`) agents** send no `previousAgentId`.
