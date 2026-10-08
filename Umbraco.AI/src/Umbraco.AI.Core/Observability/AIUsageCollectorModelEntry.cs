using Umbraco.AI.Core.Models;

namespace Umbraco.AI.Core.Observability;

/// <summary>
/// Immutable per-model usage totals within an <see cref="AIUsageCollectorSnapshot"/>.
/// </summary>
internal sealed record AIUsageCollectorModelEntry(
    AICapability Capability,
    string? ProviderId,
    string? ModelId,
    Guid? ProfileId,
    string? ProfileAlias,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    int CallCount,
    int UnreportedCallCount);
