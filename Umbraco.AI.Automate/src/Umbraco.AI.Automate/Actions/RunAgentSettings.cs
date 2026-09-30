using Umbraco.Automate.Core.Settings;

namespace Umbraco.AI.Automate.Actions;

/// <summary>
/// Settings for the <see cref="RunAgentAction"/>.
/// </summary>
public sealed class RunAgentSettings
{
    /// <summary>
    /// Gets or sets the ID of the AI agent to run.
    /// </summary>
    [Field(Label = "Agent", Description = "The AI agent to execute.",
        EditorUiAlias = "Uai.PropertyEditorUi.AgentPicker",
        EditorConfig = """[{ "alias": "surfaceId", "value": "automations" }]""")]
    public Guid AgentId { get; set; }

    /// <summary>
    /// Gets or sets the message to send to the agent. Supports binding syntax.
    /// </summary>
    [Field(Label = "Message", Description = "The message to send to the AI agent. Supports ${ binding } syntax.", SupportsBindings = true, SortOrder = 1)]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets which tool calls the agent may make, as a <see cref="RunAgentToolPermissions"/> name.
    /// </summary>
    [Field(
        Label = "Tool permissions",
        Description = "Read only: the agent can look things up but can't make any changes. "
            + "Changes that don't need approval: it can also make changes like editing drafts, "
            + "but changes that need approval, like publishing or deleting, are blocked.",
        EditorUiAlias = "Umb.PropertyEditorUi.Dropdown",
        EditorConfig = """
            [{ "alias": "items", "value": [
                { "name": "Read only", "value": "ReadOnly" },
                { "name": "Changes that don't need approval", "value": "NoApprovalRequired" }
            ] }]
            """,
        SortOrder = 2)]
    public string ToolPermissions { get; set; } = nameof(RunAgentToolPermissions.ReadOnly);
}
