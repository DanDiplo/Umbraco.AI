[Plan folder](docs/plans/pluggable-agent-selection/README.md) | [Idea doc](docs/ideas/pluggable-routing.md)

## Why the change

Copilot's "Auto" agent pick was one hard-coded LLM classifier that only saw the last message's text. This makes it an ordered, pluggable chain of selectors, so developers can drive the pick with their own business rules while today's behaviour stays the default.

## Special things to note

- **Needs a decision:** a selector that throws `OperationCanceledException` *without* the request actually being cancelled (for example an HTTP timeout inside a custom LLM selector) is not skipped. It fails the whole Copilot request (`AIAgentSelectionService.cs:115`). This matches SPEC guarantee 5 literally and today's behaviour, since the old classifier caught nothing either. The alternative is to skip it too, using `when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)`.
- **Needs a decision:** a selector's `Reason` and `SelectorId` are written word for word into `AIAuditLog.Metadata`, and `AIAuditLogRedactor` does not touch Metadata. The built-in selectors write no reason, and the XML docs tell selector authors to keep it short and free of personal data (`AIAgentSelectionResult.cs:12`). Decide whether that docs-only guard is enough (GDPR), or whether the reason should go through the redactor.
- The obsolete `IAIAgentService.SelectAgentForPromptAsync` now proxies to the new service (`AIAgentService.cs:246`, via `StaticServiceProvider` to avoid a DI cycle). It picks the same agent for the same input. Two side effects follow from the new design: it now publishes `AIAgentSelectedNotification`, and a throwing classifier now falls back to the first candidate instead of throwing.
- `AIAgentService` (internal) lost three constructor parameters that only the old classifier used. `StreamAgentAGUIController` gained a new DI constructor, and its two old public constructors are `[Obsolete]` for v20. They resolve the new dependencies through `StaticServiceProvider`.
- The live `agent_selected` event leaves the `reason` key out when it is null (the AG-UI serializer skips nulls). SPEC's example shows `"reason": null`. The frontend type treats it as optional.
- On `auto`, the controller converts messages a second time for selection. The converter is a pure mapping with no file storage, so this only costs one more base64 decode of attachments. A malformed attachment on an `auto` request now fails before streaming starts instead of inside the run. It already failed before this PR.
- On follow-up turns, attachments reach selectors as `UriContent` links, not bytes.
- A Copilot Workspace conversation reopened from history has no saved pick, so its first new turn sends no `previousAgentId`.
- Cancelling a run now keeps the previous pick (`run.controller.ts:181`). Only a conversation reset clears it.
- **Live-verified on the v18 demo site:** a custom selector driving the pick, `selectorId`/`reason` in the event and the audit row, the notification firing, the LLM default, sticky being off by default, sticky keeping the agent across turns and after Cancel, and a fresh conversation sending no previous pick. **Not live-verified:** resuming after a tool approval (S5 AC5). No demo agent has an approval-gated tool, so this rests on code review and the transport code.
- No database migrations. No new Management API routes, and no OpenAPI client change.
- **Backport:** v17 is in active support. Plan: port to `v17/dev` with the backport skill after this merges. The Umbraco.Docs "Extending > Agent selection" page is also a follow-up.

## Change outline

The new extension point and its types, all public, in `Umbraco.AI.Agent.Core/Agents/Selection/`:

```csharp
public interface IAIAgentSelector
{
    // null = "no opinion", the next selector decides
    Task<AIAgentSelectionResult?> SelectAgentAsync(AIAgentSelectionRequest request, CancellationToken ct = default);
}

public sealed record AIAgentSelectionResult(AIAgent Agent, string SelectorId, string? Reason);

public sealed class AIAgentSelectionRequest
{
    IReadOnlyList<AIAgent> CandidateAgents;      // active + in scope, already filtered
    IReadOnlyList<ChatMessage> Messages;         // full conversation incl. attachments
    AgentAvailabilityContext AvailabilityContext;
    IReadOnlyList<AIRequestContextItem> ContextItems;
    string SurfaceId;
    IReadOnlyList<Guid> UserGroupIds;
    IReadOnlyList<AIFrontendTool> FrontendTools;
    AIAgent? PreviousAgent;                      // previous turn's pick, only if still a candidate
}

public static class AIAgentSelectorIds { Llm = "llm", Sticky = "sticky", OnlyCandidate = "only-candidate", Fallback = "fallback" }

public sealed class AIAgentSelectedNotification : StatefulNotification   // observe only
{
    AIAgentSelectionResult Selection; AIAgentSelectionRequest Request; EventMessages Messages;
}
```

Registration. `LLMAgentSelector` is the only default entry. `StickyAgentSelector` ships but is opt-in:

```diff
 // UmbracoBuilderExtensions.AddUmbracoAIAgentCore
+builder.Services.AddSingleton<IAIAgentSelectionService, AIAgentSelectionService>();
+builder.AIAgentSelectors()
+    .Append<LLMAgentSelector>();

 // opt-in, in a site's own composer:
+builder.AIAgentSelectors().InsertBefore<LLMAgentSelector, StickyAgentSelector>();
```

What `AIAgentSelectionService.SelectAgentAsync` does. Scope filtering always runs first, so a selector can never reach a ruled-out agent:

```
candidates = agents for surface where IsActive && scope allows      (moved from SelectAgentForPromptAsync)
if none            -> return null                                    (no notification)
request            = { candidates, messages, context, user groups, previous pick if still a candidate, ... }
if one candidate   -> pick it, "only-candidate"
else for each selector in AIAgentSelectors() order:
       result null           -> next
       agent not a candidate -> log warning, next
       throws                -> log error, next     (OperationCanceledException propagates)
       otherwise             -> pick the matching candidate instance
if nobody decided  -> first candidate, "fallback"
publish AIAgentSelectedNotification(selection, request)
```

Call chain for an `auto` request:

```diff
 POST /agents/auto/stream-agui
   StreamAgentAGUIController.StreamAgentAGUI
-    IAIAgentService.SelectAgentForPromptAsync(lastUserMessageText, surface, context)
+    IAIAgentSelectionService.SelectAgentAsync(input)          // full messages, context items, tools,
+                                                              // forwardedProps.previousAgentId (bad -> null)
-    IAIAgentService.StreamAgentAGUIAsync(agentId, request, tools)
+    IAIAgentService.StreamAgentAGUIAsync(agentId, request, tools,
+        new AIAgentExecutionOptions { Selection = selection })  // -> SelectorId/SelectionReason in LogKeys
+                                                                //    -> AIAuditLog.Metadata
     prepend CUSTOM agent_selected
```

`agent_selected` event value:

```diff
 { "agentId": "...", "agentName": "...", "agentAlias": "...",
+  "selectorId": "llm | sticky | only-candidate | fallback | <custom>",
+  "reason": "..." }    // omitted when null
```

Frontend (`Umbraco.AI.Agent.UI` + transport). In Auto mode the browser echoes back the previous pick:

```diff
 UaiRunController (run.controller.ts)
   sendUserMessage / regenerate / both resume paths
-    client.sendMessage(messages, tools, context, resume)
+    client.sendMessage(messages, tools, context, resume, previousAgentIdForRequest())  // only when agent is "auto"
   onCustomEvent("agent_selected") -> resolvedAgent (+ selectorId, reason)
   resetConversation() -> clears the pick
-  abortRun()         -> cleared the pick
+  abortRun()         -> keeps the pick

 UaiAgentClient.sendMessage
-  forwardedProps: resume?.length ? { resume } : undefined
+  forwardedProps: { resume?, previousAgentId? } or undefined when both are empty
```

Tests: 76 new specs across `Agents/Selection/*` and `Api/StreamAgentAGUIControllerAutoSelectionTests.cs`. Agent unit tests now 298/298, integration 3/3. Automate and Agent.Deploy still build.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
