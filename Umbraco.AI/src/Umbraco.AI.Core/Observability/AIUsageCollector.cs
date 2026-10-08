using Microsoft.Extensions.AI;
using Umbraco.AI.Core.Models;

namespace Umbraco.AI.Core.Observability;

/// <summary>
/// Thread-safe accumulator of token usage for the AI calls made within an <see cref="AIUsageCollectionScope"/>.
/// Calls are grouped by capability, provider, model and profile. Tool calls may run concurrently,
/// so all access is guarded by a lock.
/// </summary>
internal sealed class AIUsageCollector
{
    private readonly object _lock = new();
    private readonly Dictionary<GroupKey, Group> _groups = new();

    /// <summary>
    /// Records one AI call. A null <paramref name="usage"/>, or one with no token counts at all,
    /// counts the call as unreported and adds no tokens (unknown is not zero). Unknown provider or model stays null.
    /// </summary>
    public void RecordCall(
        AICapability capability,
        string? providerId,
        string? modelId,
        Guid? profileId,
        string? profileAlias,
        UsageDetails? usage)
    {
        var key = new GroupKey(capability, providerId, modelId, profileId);

        lock (_lock)
        {
            if (!_groups.TryGetValue(key, out var group))
            {
                group = new Group();
                _groups[key] = group;
            }

            group.ProfileAlias ??= profileAlias;
            group.CallCount++;

            if (usage is null
                || (usage.InputTokenCount is null && usage.OutputTokenCount is null && usage.TotalTokenCount is null))
            {
                group.UnreportedCallCount++;
                return;
            }

            var input = usage.InputTokenCount ?? 0;
            var output = usage.OutputTokenCount ?? 0;

            group.InputTokens += input;
            group.OutputTokens += output;
            group.TotalTokens += usage.TotalTokenCount ?? input + output;
        }
    }

    /// <summary>
    /// Returns an immutable snapshot of everything recorded so far.
    /// </summary>
    public AIUsageCollectorSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var models = _groups
                .Select(pair => new AIUsageCollectorModelEntry(
                    pair.Key.Capability,
                    pair.Key.ProviderId,
                    pair.Key.ModelId,
                    pair.Key.ProfileId,
                    pair.Value.ProfileAlias,
                    ClampToInt(pair.Value.InputTokens),
                    ClampToInt(pair.Value.OutputTokens),
                    ClampToInt(pair.Value.TotalTokens),
                    pair.Value.CallCount,
                    pair.Value.UnreportedCallCount))
                .OrderBy(e => e.Capability)
                .ThenBy(e => e.ProviderId, StringComparer.Ordinal)
                .ThenBy(e => e.ModelId, StringComparer.Ordinal)
                .ThenBy(e => e.ProfileId)
                .ToList();

            return new AIUsageCollectorSnapshot(
                ClampToInt(_groups.Values.Sum(g => g.InputTokens)),
                ClampToInt(_groups.Values.Sum(g => g.OutputTokens)),
                ClampToInt(_groups.Values.Sum(g => g.TotalTokens)),
                _groups.Values.Sum(g => g.CallCount),
                _groups.Values.Sum(g => g.UnreportedCallCount),
                models);
        }
    }

    private static int ClampToInt(long value) => (int)Math.Clamp(value, 0, int.MaxValue);

    private readonly record struct GroupKey(AICapability Capability, string? ProviderId, string? ModelId, Guid? ProfileId);

    private sealed class Group
    {
        public string? ProfileAlias { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long TotalTokens { get; set; }
        public int CallCount { get; set; }
        public int UnreportedCallCount { get; set; }
    }
}
