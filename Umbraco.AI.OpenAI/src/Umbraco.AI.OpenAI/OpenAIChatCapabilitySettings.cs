using System.Text.Json.Serialization;
using Umbraco.AI.Core.EditableModels;
using Umbraco.AI.Core.Serialization;

namespace Umbraco.AI.OpenAI;

/// <summary>
/// Provider-declared, profile-level chat settings for OpenAI (surfaced on the profile editor and
/// applied to each request).
/// </summary>
public class OpenAIChatCapabilitySettings
{
    /// <summary>
    /// Constrains the reasoning effort for reasoning-capable models (the o-series, GPT-5 and GPT-6 Luna).
    /// Leave empty for the model default.
    /// </summary>
    /// <remarks>
    /// The schema is shared across models. GPT-6 Luna supports none/low/medium/high/xhigh/max;
    /// minimal is retained for existing models and maps to low on Luna. The xhigh/max levels are
    /// applied only to Luna, using the SDK's extensible string enum. Leave empty for the model default.
    /// </remarks>
    [AIField(
        Label = "Reasoning effort",
        Description = "Leave empty for the model default. GPT-6 Luna supports none, low, medium, high, xhigh and max; minimal maps to low. Other reasoning models retain none, minimal, low, medium and high; xhigh/max are ignored on those models.",
        EditorUiAlias = "Umb.PropertyEditorUi.Dropdown",
        EditorConfig = "[{\"alias\":\"multiple\",\"value\":false},{\"alias\":\"items\",\"value\":[\"none\",\"minimal\",\"low\",\"medium\",\"high\",\"xhigh\",\"max\"]}]",
        SortOrder = 1)]
    [JsonConverter(typeof(DropdownStringJsonConverter))]
    public string? ReasoningEffort { get; set; }
}
