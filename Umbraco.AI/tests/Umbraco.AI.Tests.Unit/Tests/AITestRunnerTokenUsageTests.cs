// S1, S2, S3 — Graders receive run token usage and per-model breakdown.
// Entry point: the real AITestRunner.ExecuteTestAsync, with a fake IAITestFeature that reports
// calls into the ambient collector through a real AIOperationTracker, and a recording grader that
// captures the outcome it receives.
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.AI.Core;
using Umbraco.AI.Core.Analytics;
using Umbraco.AI.Core.Analytics.Usage;
using Umbraco.AI.Core.AuditLog;
using Umbraco.AI.Core.EditableModels;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Observability;
using Umbraco.AI.Core.RuntimeContext;
using Umbraco.AI.Core.Tests;

namespace Umbraco.AI.Tests.Unit.Tests;

public class AITestRunnerTokenUsageTests
{
    private const string GraderTypeId = "recording-grader";

    private static readonly Guid ProfileA = Guid.NewGuid();
    private static readonly Guid ProfileB = Guid.NewGuid();

    /// <summary>
    /// One AI call as a feature would make it: tracked through a real tracker, with a fresh runtime
    /// context per call so each call carries its own provider/model/profile identity.
    /// </summary>
    private sealed record CallSpec(
        string ProviderId,
        string ModelId,
        Guid ProfileId,
        string ProfileAlias,
        AICapability Capability = AICapability.Chat,
        int? InputTokens = null,
        int? OutputTokens = null,
        int? TotalTokens = null)
    {
        public UsageDetails? Usage => InputTokens is null && OutputTokens is null && TotalTokens is null
            ? null
            : new UsageDetails { InputTokenCount = InputTokens, OutputTokenCount = OutputTokens, TotalTokenCount = TotalTokens };
    }

    private static CallSpec ChatCall(string model, Guid profileId, string alias, int total) =>
        new("openai", model, profileId, alias, TotalTokens: total);

    private static async Task ReportCallAsync(CallSpec call)
    {
        var runtimeContext = new AIRuntimeContext([]);
        runtimeContext.SetValue(Constants.ContextKeys.ProfileId, call.ProfileId);
        runtimeContext.SetValue(Constants.ContextKeys.ProfileAlias, call.ProfileAlias);
        runtimeContext.SetValue(Constants.ContextKeys.ProviderId, call.ProviderId);
        runtimeContext.SetValue(Constants.ContextKeys.ModelId, call.ModelId);

        var contextAccessor = new Mock<IAIRuntimeContextAccessor>();
        contextAccessor.Setup(x => x.Context).Returns(runtimeContext);

        var auditOptions = new Mock<IOptionsMonitor<AIAuditLogOptions>>();
        auditOptions.Setup(x => x.CurrentValue).Returns(new AIAuditLogOptions { Enabled = false });

        var analyticsOptions = new Mock<IOptionsMonitor<AIAnalyticsOptions>>();
        analyticsOptions.Setup(x => x.CurrentValue).Returns(new AIAnalyticsOptions { Enabled = false });

        var tracker = new AIOperationTracker(
            contextAccessor.Object,
            new Mock<IAIAuditLogService>().Object,
            new Mock<IAIAuditLogFactory>().Object,
            auditOptions.Object,
            new Mock<IAIUsageRecordingService>().Object,
            new Mock<IAIUsageRecordFactory>().Object,
            analyticsOptions.Object,
            NullLogger<AIOperationTracker>.Instance);

        await tracker.TrackAsync(
            new AIOperationDescriptor { Capability = call.Capability, PromptData = "prompt" },
            _ => Task.FromResult(new AITrackedOperationResult<string> { Result = "ok", Usage = call.Usage }),
            CancellationToken.None);
    }

    private static async Task ReportAsync(params CallSpec[] calls)
    {
        foreach (var call in calls)
        {
            await ReportCallAsync(call);
        }
    }

    /// <summary>
    /// Runs the real runner once against a scripted feature and a recording grader.
    /// </summary>
    private sealed class RunnerHarness
    {
        public AITestOutcome? OutcomeSeenByGrader { get; private set; }
        public AITestRun SavedRun { get; private set; } = null!;

        public RunnerHarness(Func<Task> featureScript, Func<Task>? graderScript = null)
        {
            var runRepository = new Mock<IAITestRunRepository>();
            runRepository
                .Setup(r => r.SaveAsync(It.IsAny<AITestRun>(), It.IsAny<CancellationToken>()))
                .Callback<AITestRun, CancellationToken>((run, _) => SavedRun = run)
                .ReturnsAsync((AITestRun run, CancellationToken _) => run);

            var feature = new ScriptedFeature(featureScript);
            var grader = new RecordingGrader(graderScript, outcome => OutcomeSeenByGrader = outcome);

            var runner = new AITestRunner(
                runRepository.Object,
                new Mock<IAITestTranscriptRepository>().Object,
                new AITestFeatureCollection(() => [feature]),
                new AITestGraderCollection(() => [grader]));

            var test = new AITest
            {
                Id = Guid.NewGuid(),
                Alias = "t",
                Name = "t",
                TestFeatureId = feature.Id,
                TestTargetId = Guid.NewGuid(),
                RunCount = 1,
                Graders = [new AITestGraderConfig { GraderTypeId = GraderTypeId, Name = "g" }],
            };

            runner.ExecuteTestAsync(test).GetAwaiter().GetResult();
        }
    }

    private sealed class ScriptedFeature(Func<Task> script) : IAITestFeature
    {
        public string Id => "scripted";
        public string Name => "Scripted";
        public string Description => "Runs a script that reports AI calls";
        public string Category => "test";
        public Type? ConfigType => null;
        public AIEditableModelSchema? GetConfigSchema() => null;
        public string ExtractOutputValue(AITestTranscript transcript) => string.Empty;

        public async Task<AITestTranscript> ExecuteAsync(
            AITest test,
            int runNumber,
            Guid? profileIdOverride,
            IEnumerable<Guid>? contextIdsOverride,
            IEnumerable<Guid>? guardrailIdsOverride,
            CancellationToken cancellationToken)
        {
            await script();
            return new AITestTranscript { RunId = Guid.Empty, FinalOutput = JsonDocument.Parse("{}").RootElement };
        }
    }

    private sealed class RecordingGrader(Func<Task>? script, Action<AITestOutcome> record) : IAITestGrader
    {
        public string Id => GraderTypeId;
        public string Name => "Recording";
        public string Description => "Captures the outcome it receives";
        public AIGraderType Type => AIGraderType.CodeBased;
        public Type? ConfigType => null;
        public AIEditableModelSchema? GetConfigSchema() => null;

        public async Task<AITestGraderResult> GradeAsync(
            AITestTranscript transcript,
            AITestOutcome outcome,
            AITestGraderConfig graderConfig,
            CancellationToken cancellationToken)
        {
            record(outcome);
            if (script is not null)
            {
                await script();
            }

            return new AITestGraderResult { GraderId = graderConfig.Id, Passed = true, Score = 1.0 };
        }
    }

    public class GivenOneCallWithUsage
    {
        private readonly RunnerHarness _harness = new(() => ReportAsync(
            new CallSpec("openai", "gpt-x", ProfileA, "p1", InputTokens: 100, OutputTokens: 20, TotalTokens: 120)));

        private AITestTokenUsage Usage => _harness.OutcomeSeenByGrader!.TokenUsage!;

        [Fact]
        public void GraderReceivesInputTokens() => Usage.InputTokens.ShouldBe(100);

        [Fact]
        public void GraderReceivesOutputTokens() => Usage.OutputTokens.ShouldBe(20);

        [Fact]
        public void GraderReceivesTotalTokens() => Usage.TotalTokens.ShouldBe(120);

        [Fact]
        public void GraderReceivesTheModelIdentity() =>
            Usage.Models.Select(m => (m.ProviderId, m.ModelId, m.ProfileId, m.ProfileAlias))
                .ShouldBe([("openai", "gpt-x", (Guid?)ProfileA, "p1")]);

        [Fact]
        public void RunOutcomeIsPersistedWithUsage() => _harness.SavedRun.Outcome!.TokenUsage.ShouldNotBeNull();
    }

    public class GivenThreeCallsWithUsage
    {
        private readonly AITestTokenUsage _usage;

        public GivenThreeCallsWithUsage()
        {
            var harness = new RunnerHarness(() => ReportAsync(
                ChatCall("gpt-x", ProfileA, "p1", 10),
                ChatCall("gpt-x", ProfileA, "p1", 20),
                ChatCall("gpt-x", ProfileA, "p1", 30)));
            _usage = harness.OutcomeSeenByGrader!.TokenUsage!;
        }

        [Fact]
        public void SumsTheTotals() => _usage.TotalTokens.ShouldBe(60);

        [Fact]
        public void CountsTheCalls() => _usage.CallCount.ShouldBe(3);
    }

    public class GivenCallsToTwoModels
    {
        private readonly AITestTokenUsage _usage;

        public GivenCallsToTwoModels()
        {
            var harness = new RunnerHarness(() => ReportAsync(
                ChatCall("model-a", ProfileA, "pa", 10),
                ChatCall("model-b", ProfileB, "pb", 20),
                new CallSpec("openai", "model-b", ProfileB, "pb", AICapability.Embedding, TotalTokens: 5)));
            _usage = harness.OutcomeSeenByGrader!.TokenUsage!;
        }

        [Fact]
        public void HasAnEntryPerModelAndCapability() => _usage.Models.Count.ShouldBe(3);

        [Fact]
        public void TopLevelTotalEqualsSumOfEntries() => _usage.TotalTokens.ShouldBe(_usage.Models.Sum(m => m.TotalTokens));

        [Fact]
        public void EachEntryHoldsOnlyItsOwnTokens() =>
            _usage.Models.Single(m => m.ModelId == "model-a").TotalTokens.ShouldBe(10);

        [Fact]
        public void EntriesCarryTheirCapability() =>
            _usage.Models.Count(m => m.Capability == AICapability.Embedding).ShouldBe(1);
    }

    public class GivenOneReportedAndOneUnreportedCall
    {
        private readonly AITestTokenUsage _usage;

        public GivenOneReportedAndOneUnreportedCall()
        {
            var harness = new RunnerHarness(() => ReportAsync(
                ChatCall("model-a", ProfileA, "pa", 50),
                new CallSpec("openai", "model-b", ProfileB, "pb")));
            _usage = harness.OutcomeSeenByGrader!.TokenUsage!;
        }

        [Fact]
        public void CountsTheUnreportedCall() => _usage.UnreportedCallCount.ShouldBe(1);

        [Fact]
        public void TotalIsALowerBound() => _usage.TotalTokens.ShouldBe(50);

        [Fact]
        public void ModelEntryCountsTheUnreportedCall() =>
            _usage.Models.Single(m => m.ModelId == "model-b").UnreportedCallCount.ShouldBe(1);
    }

    public class GivenNoTrackedCalls
    {
        private readonly RunnerHarness _harness = new(() => Task.CompletedTask);

        [Fact]
        public void TokenUsageIsNull() => _harness.OutcomeSeenByGrader!.TokenUsage.ShouldBeNull();
    }

    public class GivenAGraderThatMakesItsOwnCall
    {
        private readonly RunnerHarness _harness = new(
            () => ReportAsync(ChatCall("model-a", ProfileA, "pa", 10)),
            () => ReportAsync(ChatCall("judge", ProfileB, "pb", 999)));

        [Fact]
        public void GraderCallIsNotCounted() => _harness.SavedRun.Outcome!.TokenUsage!.TotalTokens.ShouldBe(10);
    }

    public class GivenAFeatureThatThrowsAfterACall
    {
        private readonly RunnerHarness _harness = new(async () =>
        {
            await ReportAsync(ChatCall("model-a", ProfileA, "pa", 10));
            throw new InvalidOperationException("boom");
        });

        [Fact]
        public void RunStatusIsError() => _harness.SavedRun.Status.ShouldBe(AITestRunStatus.Error);

        [Fact]
        public void RunHasNoOutcome() => _harness.SavedRun.Outcome.ShouldBeNull();
    }
}
