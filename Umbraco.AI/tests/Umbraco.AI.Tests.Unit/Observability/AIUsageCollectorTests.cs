using Microsoft.Extensions.AI;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Observability;

// S1, S2 — Run token totals and per-model breakdown at the collector level (AC1.9, AC1.11).
namespace Umbraco.AI.Tests.Unit.Observability;

public class AIUsageCollectorTests
{
    private static readonly Guid ProfileId = Guid.NewGuid();

    private static void Record(AIUsageCollector collector, string? modelId, UsageDetails? usage) =>
        collector.RecordCall(AICapability.Chat, "openai", modelId, ProfileId, "profile", usage);

    private static UsageDetails Usage(long total) =>
        new() { InputTokenCount = total / 2, OutputTokenCount = total - total / 2, TotalTokenCount = total };

    public class GivenTwoCallsToTheSameModel
    {
        private readonly AIUsageCollectorSnapshot _snapshot;

        public GivenTwoCallsToTheSameModel()
        {
            var collector = new AIUsageCollector();
            Record(collector, "gpt", Usage(10));
            Record(collector, "gpt", Usage(20));
            _snapshot = collector.GetSnapshot();
        }

        [Fact]
        public void GroupsThemIntoOneEntry() => _snapshot.Models.Count.ShouldBe(1);

        [Fact]
        public void SumsTheirTokens() => _snapshot.TotalTokens.ShouldBe(30);
    }

    public class GivenCallsToTwoModels
    {
        private readonly AIUsageCollectorSnapshot _snapshot;

        public GivenCallsToTwoModels()
        {
            var collector = new AIUsageCollector();
            Record(collector, "model-a", Usage(10));
            Record(collector, "model-b", Usage(20));
            _snapshot = collector.GetSnapshot();
        }

        [Fact]
        public void KeepsAnEntryPerModel() => _snapshot.Models.Count.ShouldBe(2);

        [Fact]
        public void KeepsOnlyItsOwnTokensInEachEntry() =>
            _snapshot.Models.Single(m => m.ModelId == "model-a").TotalTokens.ShouldBe(10);

        [Fact]
        public void TotalsTheSumOfTheEntries() =>
            _snapshot.TotalTokens.ShouldBe(_snapshot.Models.Sum(m => m.TotalTokens));

        [Fact]
        public void OrdersEntriesByModel() =>
            _snapshot.Models.Select(m => m.ModelId).ShouldBe(["model-a", "model-b"]);
    }

    public class GivenNoTotalFromTheProvider
    {
        private readonly AIUsageCollectorSnapshot _snapshot;

        public GivenNoTotalFromTheProvider()
        {
            var collector = new AIUsageCollector();
            Record(collector, "gpt", new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3, TotalTokenCount = null });
            _snapshot = collector.GetSnapshot();
        }

        [Fact]
        public void UsesInputPlusOutputAsTheTotal() => _snapshot.TotalTokens.ShouldBe(10);
    }

    public class GivenACallWithNoUsage
    {
        private readonly AIUsageCollectorSnapshot _snapshot;

        public GivenACallWithNoUsage()
        {
            var collector = new AIUsageCollector();
            Record(collector, "gpt", null);
            _snapshot = collector.GetSnapshot();
        }

        [Fact]
        public void CountsItAsUnreported() => _snapshot.UnreportedCallCount.ShouldBe(1);

        [Fact]
        public void StillCountsTheCall() => _snapshot.CallCount.ShouldBe(1);
    }

    public class GivenAUsageObjectWithNoCounts
    {
        private readonly AIUsageCollectorSnapshot _snapshot;

        public GivenAUsageObjectWithNoCounts()
        {
            var collector = new AIUsageCollector();
            Record(collector, "gpt", new UsageDetails());
            _snapshot = collector.GetSnapshot();
        }

        [Fact]
        public void CountsItAsUnreported() => _snapshot.UnreportedCallCount.ShouldBe(1);
    }

    public class GivenACallWithUnknownProviderAndModel
    {
        private readonly AIUsageCollectorSnapshot _snapshot;

        public GivenACallWithUnknownProviderAndModel()
        {
            var collector = new AIUsageCollector();
            collector.RecordCall(AICapability.Chat, null, null, null, null, Usage(10));
            _snapshot = collector.GetSnapshot();
        }

        [Fact]
        public void StillCountsTheCall() => _snapshot.CallCount.ShouldBe(1);

        [Fact]
        public void LandsInOneEntryWithANullModel() => _snapshot.Models.Single().ModelId.ShouldBeNull();
    }

    public class GivenNestedScopes : IDisposable
    {
        private readonly AIUsageCollectionScope _outer;

        public GivenNestedScopes()
        {
            _outer = AIUsageCollectionScope.Begin();
            using var inner = AIUsageCollectionScope.Begin();
            Record(inner.Collector, "gpt", Usage(10));
        }

        public void Dispose() => _outer.Dispose();

        [Fact]
        public void RecordsTheCallOnlyInTheInnerCollector() =>
            _outer.Collector.GetSnapshot().CallCount.ShouldBe(0);

        [Fact]
        public void RestoresTheOuterCollectorAsCurrent() =>
            AIUsageCollectionScope.Current.ShouldBeSameAs(_outer.Collector);
    }

    public class GivenTwoConcurrentScopes
    {
        private readonly int[] _callCounts;

        public GivenTwoConcurrentScopes()
        {
            using var barrier = new Barrier(2);

            Task<int> Flow(int calls) => Task.Run(async () =>
            {
                using var scope = AIUsageCollectionScope.Begin();
                barrier.SignalAndWait(); // both scopes are open before either records
                for (var i = 0; i < calls; i++)
                {
                    await Task.Yield();
                    AIUsageCollectionScope.Current!.RecordCall(AICapability.Chat, "openai", "gpt", null, null, Usage(2));
                }
                return scope.Collector.GetSnapshot().CallCount;
            });

            _callCounts = Task.WhenAll(Flow(3), Flow(5)).GetAwaiter().GetResult();
        }

        [Fact]
        public void KeepsEachFlowsCallsSeparate() => _callCounts.ShouldBe([3, 5]);
    }

    public class GivenNoOpenScope
    {
        [Fact]
        public void HasNoCurrentCollector() => AIUsageCollectionScope.Current.ShouldBeNull();
    }
}
