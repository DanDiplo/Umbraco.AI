namespace Umbraco.AI.Core.Observability;

/// <summary>
/// Which provider, model and profile a tracked operation ran against, captured when the operation
/// begins. The shared runtime context is overwritten by nested AI calls, so it must not be re-read
/// at completion.
/// </summary>
internal sealed record AIOperationIdentity(
    string? ProviderId,
    string? ModelId,
    Guid? ProfileId,
    string? ProfileAlias);
