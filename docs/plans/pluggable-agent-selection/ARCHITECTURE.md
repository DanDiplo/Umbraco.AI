# Architecture

## Extension points

**An ordered collection of agent selectors, in `Umbraco.AI.Agent.Core`.**

```csharp
namespace Umbraco.AI.Agent.Core.Agents.Selection;

public interface IAIAgentSelector
{
    /// Return null for "no opinion" - the next selector in the collection decides.
    Task<AIAgentSelectionResult?> SelectAgentAsync(
        AIAgentSelectionRequest request,
        CancellationToken cancellationToken = default);
}

public class AIAgentSelectorCollectionBuilder
    : OrderedCollectionBuilderBase<AIAgentSelectorCollectionBuilder, AIAgentSelectorCollection, IAIAgentSelector>
{
    protected override AIAgentSelectorCollectionBuilder This => this;
}
```

Registered through `builder.AIAgentSelectors()` (a new `UmbracoBuilderExtensions.Selectors.cs`,
same shape as `UmbracoBuilderExtensions.Surfaces.cs`). Developers add their own with
`Append` / `InsertBefore<LLMAgentSelector, MySelector>()`.

This copies the closest sibling: `IAIGuardrailResolver` + `AIGuardrailResolverCollectionBuilder`
in Core, an ordered chain where each entry adds to (or skips) the result. Using the same Umbraco
collection-builder mechanism means ordering, `Exclude<T>`, and DI lifetimes all work the way
Umbraco developers already expect.

**Default registration:** `LLMAgentSelector` only. `StickyAgentSelector` ships but is **not**
registered, so out-of-the-box behaviour is unchanged. Turning it on is one line:

```csharp
builder.AIAgentSelectors().InsertBefore<LLMAgentSelector, StickyAgentSelector>();
```

### Types

```csharp
public sealed class AIAgentSelectionRequest
{
    // Active + scope-available agents, in the same order SelectAgentForPromptAsync used.
    // Selectors may only return one of these.
    public required IReadOnlyList<AIAgent> CandidateAgents { get; init; }

    // Full conversation as M.E.AI messages (history + attachments), converted once.
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    // Surface / section / entity type (what scope rules use).
    public required AgentAvailabilityContext AvailabilityContext { get; init; }

    // Raw request context items (entity key, content type, etc.) - everything the frontend
    // sent, not just the three fields AvailabilityContext extracts. This is what lets
    // business rules see "the context" without us guessing which fields they need.
    public required IReadOnlyList<AIRequestContextItem> ContextItems { get; init; }

    public required string SurfaceId { get; init; }

    public required IReadOnlyList<Guid> UserGroupIds { get; init; }

    public IReadOnlyList<AIFrontendTool> FrontendTools { get; init; } = [];

    // The agent picked on the previous turn, resolved against CandidateAgents.
    // Null if the browser sent nothing, sent garbage, or the agent is no longer allowed.
    public AIAgent? PreviousAgent { get; init; }
}

public sealed record AIAgentSelectionResult(AIAgent Agent, string SelectorId, string? Reason);
```

### Orchestration: `IAIAgentSelectionService`

A small new service owns the chain, so `AIAgentService` (already large) doesn't grow further.

```csharp
public interface IAIAgentSelectionService
{
    // Null only when there are no candidate agents at all.
    Task<AIAgentSelectionResult?> SelectAgentAsync(
        AIAgentSelectionInput input,
        CancellationToken cancellationToken = default);
}
```

`AIAgentSelectionInput` is what callers have before filtering (surface, availability context,
messages, context items, frontend tools, optional previous agent ID). The service turns it into an
`AIAgentSelectionRequest`:

1. Get agents for the surface, filter to active + `AIAgentScopeValidator.IsAgentAvailable`. This is
   today's logic, moved, not changed. **Filtering always runs before any selector.**
2. 0 candidates -> `null`.
3. 1 candidate -> that agent, `SelectorId = "only-candidate"`. No selector runs.
4. Resolve user group IDs once. Resolve `PreviousAgent` against the candidates (an ID not in
   the list becomes `null`).
5. Run selectors in collection order. For each result:
   - `null` -> next selector.
   - Agent **not** in `CandidateAgents` -> log a warning, ignore, next selector.
   - Otherwise -> return it.
   - Selector throws (other than cancellation) -> log the error, next selector.
6. Nobody decided -> first candidate, `SelectorId = "fallback"`. Same agent today's code
   falls back to, but now recorded.
7. Publish `AIAgentSelectedNotification` with the final result (see below), then return it.
   This covers every outcome from steps 3-6 and never runs for step 2 (no candidates).

### `AIAgentSelectedNotification`

```csharp
public sealed class AIAgentSelectedNotification : StatefulNotification
{
    public AIAgentSelectedNotification(
        AIAgentSelectionResult selection,
        AIAgentSelectionRequest request,
        EventMessages messages);

    public AIAgentSelectionResult Selection { get; }   // agent, selector ID, reason
    public AIAgentSelectionRequest Request { get; }    // exactly what the selectors saw
    public EventMessages Messages { get; }
}
```

- Lives next to `AIAgentExecutingNotification` / `AIAgentExecutedNotification` in
  `Umbraco.AI.Agent.Core.Agents` and copies their shape: a `StatefulNotification` carrying
  `EventMessages`, published through `IEventAggregator.PublishAsync` by the selection service.
- **Observation only, not cancelable.** Handlers can log, count, or react. They can't change the
  pick, because selectors already do that.
- For the `only-candidate` case the service still builds an `AIAgentSelectionRequest` (it just
  runs no selectors), so `Request` is never null.
- "Selected" does not mean "ran": `AIAgentExecutingNotification` fires afterwards and can still
  cancel the run. The XML docs must say this.
- A throwing handler propagates, the same as the other agent notifications. No special
  swallowing.

`AIAgentService.SelectAgentForPromptAsync` stays, builds an input from its arguments (one user
message), calls the new service, and returns `.Agent`. Marked
`[Obsolete("Use IAIAgentSelectionService.SelectAgentAsync. Will be removed in v20")]`.

### Built-in selectors

- **`LLMAgentSelector`** (`SelectorId = "llm"`, registered by default) - today's classifier,
  moved as is. Same prompt, same input (last user message text only), same classifier profile.
  The one change: where it used to return the first agent on a failure (no profile, unparseable
  reply, unknown GUID), it now returns `null` and the service's `fallback` records it.
  Same agent picked, but the reason is now visible.
- **`StickyAgentSelector`** (`SelectorId = "sticky"`, opt-in) - returns `PreviousAgent` if set,
  otherwise `null`. No config.

### Shared agent resolution: `IAIAgentResolutionService`

Two endpoints stream an agent today, each with its own copy of "which agent runs":

| | `StreamAgentAGUIController` (Copilot, Agent.Web) | `StreamConversationAGUIController` (Workspace.Web) |
|---|---|---|
| Requested agent | Route `{agentIdOrAlias}` | `AIConversation.AgentIdOrAlias` (null or `"auto"` = Auto) |
| Surface | From AG-UI context items | Fixed `copilot-workspace` |
| Explicit agent | 404 if missing; scope check when a surface is declared | Active check only; missing/inactive falls back to Auto |
| Auto | Last inbound user message -> classifier | Same, but falls back to the last **persisted** user message (regenerate) |
| `agent_selected` event | Prepended | **Not sent** |

Selection is only half the job, so the chain alone does not remove this duplication. A second
small service in `Agents/Selection/` sits **above** `IAIAgentSelectionService` and owns the whole
"which agent runs" decision for any endpoint:

```csharp
public interface IAIAgentResolutionService
{
    Task<AIAgentResolutionResult> ResolveAgentAsync(
        AIAgentResolutionInput input,
        CancellationToken cancellationToken = default);
}

public sealed class AIAgentResolutionInput
{
    // Null, empty or "auto" (case-insensitive) means auto selection.
    public string? RequestedAgentIdOrAlias { get; init; }

    // Null only for contextless programmatic callers; auto then fails with SurfaceRequired.
    public string? SurfaceId { get; init; }

    public required AgentAvailabilityContext AvailabilityContext { get; init; }

    // Everything the selection chain needs (passed through to AIAgentSelectionInput).
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public IReadOnlyList<AIRequestContextItem> ContextItems { get; init; } = [];
    public IReadOnlyList<AIFrontendTool> FrontendTools { get; init; } = [];
    public string? PreviousAgentId { get; init; }

    // What to do when an explicitly requested agent is missing, inactive or not available
    // in this context. Copilot: Fail (today's 404). Workspace: UseAuto (today's fallback).
    public AIUnavailableAgentBehavior UnavailableAgentBehavior { get; init; } = AIUnavailableAgentBehavior.Fail;
}

public enum AIUnavailableAgentBehavior { Fail, UseAuto }

public sealed record AIAgentResolutionResult
{
    public AIAgent? Agent { get; init; }

    // Set only when the agent came from auto selection. Null for an explicit agent.
    public AIAgentSelectionResult? Selection { get; init; }

    // Set only when Agent is null.
    public AIAgentResolutionFailure? Failure { get; init; }
}

public enum AIAgentResolutionFailure { AgentNotFound, AgentNotAvailable, SurfaceRequired, NoCandidates }
```

Rules, applied identically for every caller:

1. **Explicit agent** (`RequestedAgentIdOrAlias` set, not `auto`): resolve by GUID or alias. It
   must be active **and**, when `SurfaceId` is set, pass `AIAgentScopeValidator.IsAgentAvailable`.
   Today Copilot does the scope check and Workspace does not. Otherwise:
   - `Fail` -> `AgentNotFound` (missing) or `AgentNotAvailable` (inactive or out of scope).
   - `UseAuto` -> continue as auto.
2. **Auto:** no `SurfaceId` -> `SurfaceRequired`. Otherwise call
   `IAIAgentSelectionService.SelectAgentAsync`. Null -> `NoCandidates`, else return the agent
   plus `Selection`.

Each controller maps a `Failure` to its own `ProblemDetails`, keeping today's wording and status
codes. `ProblemDetails` is ASP.NET, so the mapping stays out of Core.

**`agent_selected` event: one shared helper.** `PrependAgentSelectedEvent` moves out of
`StreamAgentAGUIController` into a public static helper in `Agent.Core/AGUI/`, e.g.
`AGUIAgentSelectionEvents.Prepend(stream, selection)`. It builds the event (including the new
`selectorId`/`reason`) in one place. Both controllers call it whenever `Selection` is non-null.
It has to live in Agent.Core, because `Copilot.Workspace.Web` does not reference `Agent.Web`.

**Workspace-specific input:**

- **Messages.** Workspace's `request.Messages` holds only the **new** inbound turn; history is
  server-side. To keep the S2 promise ("selectors see the full conversation"), the Workspace
  controller passes persisted history plus the inbound messages. This also covers today's
  regenerate fallback (the last persisted user message) for free.
  - Cost: one extra message load per auto turn. The chat history provider loads the same rows
    again for the run. Accepted for now. Sharing that load is a TODO.
- **Previous pick.** Workspace reuses the shared `run.controller.ts`, so it sends
  `forwardedProps.previousAgentId` the same way Copilot does (T2), once it starts receiving
  `agent_selected`. Server-side storage of the last pick on the conversation is a follow-up (see
  TODO).

## Data model & persistence

None. Selection is computed per request and never stored. The "previous pick" travels from the
browser on each request (see Key decisions). The selection reason is written into the agent run's
existing audit log `Metadata` dictionary, so no schema change.

## Connected systems

| System | Applies? | Why |
|--------|----------|-----|
| Audit log | **Yes** | Selection reason goes in `AIAuditLog.Metadata` via runtime context `LogKeys` (the same route `RunId`/`ThreadId` take). No schema change. |
| AG-UI `agent_selected` event | **Yes** | Gains `selectorId` and `reason`. Additive, so existing listeners keep working. |
| Agent UI library (`Umbraco.AI.Agent.UI`) | **Yes** | Sends the previous pick in `forwardedProps`, widens the `resolvedAgent$` type. |
| Copilot Workspace (`StreamConversationAGUIController`) | **Yes** | Resolves its agent through `IAIAgentResolutionService`. Gains the selector chain, the scope check on its stored agent, and the `agent_selected` event it never sent. |
| Public docs (Umbraco.Docs) | **Yes** | A developer extension point is useless if nobody can find it. Add an "Extending > Agent selection" page. |
| v17 backport | **Yes** | Both lines are in active support. Port after v18 lands. |
| Persistence / migrations | No | Nothing is stored. |
| Deploy connectors | No | No new entity. |
| Version history | No | No new entity. |
| Notifications | **`*ed` only** | `AIAgentSelectedNotification` after every `auto` pick, for watching it. No cancelable `*ing` version, because selectors are already the way to change the pick. |
| Management API / OpenAPI client | No regen | No new route or DTO. `forwardedProps` is already `JsonElement`, and the event value is untyped. |
| Localization | No | No new UI text. |

## Key decisions

- **Ordered collection builder over a single replaceable service.** Rejected: one
  `IAIAgentSelector` swapped via DI (only one package can win, and nothing composes); replacing
  `IAIAgentService` (forces copying the whole service); a notification handler (no clear ordering,
  and notifications are for watching or cancelling, not deciding).
- **Scope filtering happens in the service, before the chain, and results are checked against
  the candidates.** A third-party selector can never reach an agent the surface or context ruled
  out. Rejected: letting selectors see all agents and trusting them to filter.
- **Selectors return `null` for "no opinion"; the service owns the fallback.** Each selector stays
  single-purpose, and the fallback is recorded in one place. Rejected: a `Continue`/`Stop` result
  enum (more ceremony, no extra power).
- **A selector that throws is skipped, not fatal.** A buggy business rule shouldn't take Copilot
  down, and skipping can't break scope because filtering already ran. Cancellation still
  propagates.
- **Previous pick travels in AG-UI `forwardedProps.previousAgentId`.** The server stores no
  conversations on this line, so the browser is the only thing that knows. `forwardedProps` is the
  AG-UI spec's slot for extra data, is never shown to the model, and is already used (`resume`).
  Treated as an untrusted hint: resolved only against the candidates. Rejected: a context item
  (these can end up in the model's prompt); a new typed request field (steps outside the AG-UI
  spec).
- **Selection info reaches the audit log through a typed `AIAgentExecutionOptions.Selection`.**
  The controller calls the existing options overload of `StreamAgentAGUIAsync` with
  `new AIAgentExecutionOptions { Selection = result }`. This is the same as the default
  `new AIAgentExecutionOptions()` the plain overload already passes, plus the selection. The
  service writes `Umbraco.AI.Agent.SelectorId` and `Umbraco.AI.Agent.SelectionReason` into the
  run's additional properties and adds them to `LogKeys`. Rejected: a generic
  `AdditionalProperties` bag on options (untyped, and leaks internal context-key plumbing to
  callers).
- **The LLM classifier keeps its exact input and prompt.** Out-of-the-box picks must not change.
  Richer classifier input is a separate, behaviour-changing feature.
- **Only a `Selected` notification, no `Selecting`.** Developers can watch picks (logging,
  analytics) without writing a selector. A cancelable `Selecting` notification was rejected: it
  would be a second way to change the pick, with unclear precedence against the selector chain.
- **Sticky ships opt-in, not on by default.** Keeps today's re-pick-every-turn behaviour for
  existing sites, and still proves the previous-pick input works.
- **One resolution service for every streaming endpoint, above the selection service.**
  Selection (the chain) and resolution (explicit-or-auto, scope rules, failure kinds) are
  separate jobs. Folding resolution into `IAIAgentSelectionService` would make it handle
  explicit agents it never selects. Rejected: leaving each controller to call the selection
  service itself. That keeps the explicit-agent copies, which is exactly where the two endpoints
  drifted.
- **Per-caller policy for unavailable explicit agents (`Fail` / `UseAuto`), but a single scope
  rule.** Copilot's 404 and Workspace's fall-back-to-Auto are both deliberate product choices,
  so the option keeps them. The scope check is not optional: skipping it was the drift.

## TODO

- **Abort clears the previous pick.** `run.controller.ts` `abortRun()` resets `resolvedAgent`, so
  after a cancelled run the next request sends no `previousAgentId` and sticky loses its memory.
  Decide during build whether abort should keep it (likely yes; only `resetConversation` should
  clear it).
- **Server-side previous pick for Workspace.** Workspace conversations are persisted, so the last
  auto pick could live on the conversation row (a `LastAgentId` column, written in the same
  update that already sets `LastMessageAt`, and read for free since the row is loaded for the
  ownership check). This is more reliable than the browser hint across reloads and devices, but
  it needs a migration. Follow-up, not in this feature.
- **Share the Workspace history load.** See Workspace-specific input above.
- **Starter prompts interaction.** The starter-prompts feature (planned, not on `v18/dev` yet)
  pins a conversation to the starter's agent. That pin should send the explicit agent ID and skip
  `auto` entirely, so the two shouldn't conflict. Re-check when starter prompts land.
