# Model Facts: Structured Model Information from Packages

## Status: Under Consideration

Let packages (and core itself) supply short, structured **facts about a model**, such as estimated
CO2e, price per token, context window or a retirement warning. Umbraco.AI then shows them in a
consistent way wherever a model is chosen, starting with the profile editor's model field.

The key design choice is that packages supply **data, not UI**. Core owns how facts look, how
many show and where they appear. This keeps the profile editor tidy and the contract small enough to
keep stable across majors.

---

## Why

- **Model choice is where cost, quality and footprint get decided.** Today the profile editor's
  Model field is a plain dropdown with no context beyond the model name.
- **Packages have useful context but nowhere to put it.** The motivating case is
  [Umbraco.Community.AI.Carbon](https://github.com/mattbrailsford/Umbraco.Community.AI.Carbon). It
  estimates CO2e per model using EcoLogits, and in testing the gap between a large and a small model
  (roughly 20x per output token) was far bigger than the uncertainty on either. Showing "≈ 1.2 g CO2e
  per 1,000 output tokens" at the point of choice is the most useful place for that number.
- **Core has its own candidates.** Context window size, a "retiring on DD-MM-YYYY" warning, or
  provider notes would use the same mechanism, so this isn't built for one package.

## How it works today

- The profile Settings view (`profile-details-workspace-view.element.ts`) renders Connection and
  Model as plain `uui-select` elements inside `umb-property-layout`. There's no extension slot.
- `UAI_PROFILE_WORKSPACE_CONTEXT` and the profile model types aren't in the public
  `@umbraco-ai/core` exports.
- A package can only add a `workspaceFooterApp` or its own workspace view (tab) to
  `UmbracoAI.Workspace.Profile`. Neither sits next to the Model field. Injecting into the view's
  shadow DOM works, but depends on internal markup and would break silently.
- Providers can already return `AIModelDescriptor.Metadata` (a string dictionary) with the model
  list. That's provider-owned and unstructured, so it doesn't cover facts from other packages.

## Rejected alternative: a free-form UI slot

An `umb-extension-slot` under the Model field, where packages render any element they like. It's
simple, but:

- the layout becomes a dump of unrelated views with different styles and sizes
- accessibility and spacing are left to each package
- facts can't be reused elsewhere (dropdown options, a comparison table, Copilot)
- the public contract would be shaped by a single consumer

## Proposed shape

### Server side (Umbraco.AI.Core)

```csharp
// Registered via a collection builder, like providers and middleware.
public interface IAIModelInfoProvider
{
    Task<IEnumerable<AIModelFact>> GetFactsAsync(AIModelInfoContext context, CancellationToken cancellationToken);
}

public sealed record AIModelInfoContext(AIModelRef Model, AICapability Capability);

public sealed record AIModelFact(
    string Key,              // unique per fact, e.g. "carbon.co2ePer1kTokens"
    string Label,            // "Estimated CO2e"
    string Value,            // "≈ 1.2 g per 1,000 output tokens"
    string? Detail = null,   // tooltip, e.g. "Range 0.9–1.3 g. Estimated with EcoLogits."
    AIModelFactTone Tone = AIModelFactTone.Neutral,
    string? Url = null);     // optional "learn more"

public enum AIModelFactTone { Neutral, Positive, Warning }
```

- Facts are localizable text produced by the provider of the fact, not raw numbers. Core doesn't
  need to understand them.
- A failing or slow provider must not break the model list: time out and skip it, and log it.

### Management API

- `GET /v1/model-facts?providerId=&modelId=&capability=` returns the facts for one model, used by
  the profile editor as the selection changes (including unsaved profiles).
- Optionally, the connection's models endpoint could include facts per model later, so the
  dropdown itself can show them.

### Backoffice (core-rendered)

- Under the Model field, core renders the facts as a compact, consistent list: label, value, a
  tooltip for `Detail`, and a warning style for `Tone = Warning`.
- Core owns the rules: order (warnings first, then by provider registration order), a maximum per
  provider, and what happens when there are none (render nothing).

## What it would enable

- **Carbon:** "Estimated CO2e: ≈ 1.2 g per 1k tokens", with the range and method in the tooltip.
- **A pricing package:** "Price: $3 per 1M input tokens".
- **Core:** context window size, a retirement warning, "Doesn't support tools".
- Later, the same data in a model comparison view or for Copilot to explain model choices.

## Open questions

- Is `capability` enough context, or do providers also need the connection (for region-specific
  facts such as pricing or electricity zone)? Connection-aware facts are more useful but widen the
  contract.
- Should facts be cached per model, and for how long? Carbon's are static per data version; pricing
  might not be.
- Is there a case for core consuming `AIModelDescriptor.Metadata` through this same rendering, so
  provider metadata and package facts look the same?
- Where else should facts appear first: the model dropdown options, or only the selected model?

## When to revisit

Build this when a second real consumer appears, for example core wanting to show context window
or retirement dates, or another package beyond Carbon. Until then, Carbon can show its hint in a
`workspaceFooterApp` on the profile workspace, which needs no core changes.

Like any public API here, this would follow the usual contribution process and the "never break
a public API" rule, so the contract should be shaped by at least two real uses before it ships.
