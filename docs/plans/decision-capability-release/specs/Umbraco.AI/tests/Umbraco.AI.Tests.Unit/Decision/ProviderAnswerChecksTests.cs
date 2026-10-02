#pragma warning disable UMBRACOAI_DECISION // Exercises the experimental decision capability surface

// DR-15 — Provider answers are complete and consistent (AC1-AC9).
//
// The checks live in AIErrorClassifyingDecisionClient, inside tracking (ARCHITECTURE "Checks"), so
// every spec goes through the real factory pipeline (DecisionTrackingAndChecksHarness) rather than
// newing the classifier directly. Tolerance under test: |sum - 1| <= max(0.02, 0.005 x count).

using Umbraco.AI.Core.AuditLog;
using Umbraco.AI.Core.Decision;
using Umbraco.AI.Core.Providers.Errors;
using Umbraco.AI.Tests.Common.Fakes;

namespace Umbraco.AI.Tests.Unit.Decision;

public class ProviderAnswerChecksTests
{
    private static AIDecisionRequest One(AIDecisionQuestion question) => new() { State = "text", Questions = [question] };

    private static AIDecisionQuestion Binary() => new AIBinaryDecisionQuestion { Id = "q", Instructions = "Is it?" };

    private static AIDecisionQuestion Choice(params string[] keys) => new AIChoiceDecisionQuestion
    {
        Id = "q",
        Instructions = "Which?",
        Options = keys.Select(k => new AIDecisionOption(k)).ToList(),
    };

    private static AIDecisionQuestion Score(int levels) => new AIScoreDecisionQuestion
    {
        Id = "q",
        Instructions = "How much?",
        Levels = Enumerable.Range(0, levels).Select(i => new AIDecisionScoreLevel($"level {i}")).ToList(),
    };

    private static async Task<Func<Task<AIDecisionResponse>>> ArrangeAsync(AIDecisionQuestion question, AIDecisionAnswer answer)
    {
        var harness = await DecisionTrackingAndChecksHarness.CreateAsync(new FakeDecisionClient(_ => new AIDecisionResponse
        {
            Answers = new Dictionary<string, AIDecisionAnswer> { ["q"] = answer },
        }));
        return () => harness.Client.GetResponseAsync(One(question));
    }

    #region Happy path

    public class GivenChoiceProbabilitiesRoundedToSumPoint99
    {
        [Fact(Skip = "Pending T30")]
        public async Task Passes()
        {
            var act = await ArrangeAsync(Choice("a", "b", "c"), new AIChoiceDecisionAnswer
            {
                Choice = "a",
                Probabilities = new Dictionary<string, double> { ["a"] = 0.33, ["b"] = 0.33, ["c"] = 0.33 },
            });

            await Should.NotThrowAsync(act);
        }
    }

    #endregion

    #region Sad path

    public class GivenAnInconsistentAnswer
    {
        public static TheoryData<string, AIDecisionQuestion, AIDecisionAnswer> Cases => new()
        {
            { "true-probability above 1", Binary(), new AIBinaryDecisionAnswer { TrueProbability = 1.2 } },
            {
                "choice distribution missing an option", Choice("a", "b", "c"),
                new AIChoiceDecisionAnswer { Choice = "a", Probabilities = new Dictionary<string, double> { ["a"] = 0.6, ["b"] = 0.4 } }
            },
            {
                "choice that wasn't offered", Choice("a", "b"),
                new AIChoiceDecisionAnswer { Choice = "z", Probabilities = new Dictionary<string, double> { ["a"] = 0.5, ["b"] = 0.5 } }
            },
            {
                "choice probabilities summing to 0.8", Choice("a", "b"),
                new AIChoiceDecisionAnswer { Choice = "a", Probabilities = new Dictionary<string, double> { ["a"] = 0.5, ["b"] = 0.3 } }
            },
            {
                "score above the top level", Score(3),
                new AIScoreDecisionAnswer { Score = 2.5, Probabilities = new Dictionary<int, double> { [0] = 0.0, [1] = 0.0, [2] = 1.0 } }
            },
            {
                "score distribution with the wrong keys", Score(3),
                new AIScoreDecisionAnswer { Score = 1.0, Probabilities = new Dictionary<int, double> { [0] = 0.2, [1] = 0.6, [3] = 0.2 } }
            },
            {
                "confidence below 0", Choice("a", "b"),
                new AIChoiceDecisionAnswer { Choice = "a", Confidence = -0.1, Probabilities = new Dictionary<string, double> { ["a"] = 0.6, ["b"] = 0.4 } }
            },
        };

        [Theory(Skip = "Pending T30")]
        [MemberData(nameof(Cases))]
        public async Task ThrowsAIProviderException(string _, AIDecisionQuestion question, AIDecisionAnswer answer)
        {
            var act = await ArrangeAsync(question, answer);

            await Should.ThrowAsync<AIProviderException>(act);
        }

        [Theory(Skip = "Pending T30")]
        [MemberData(nameof(Cases))]
        public async Task RecordsAFailedCall(string _, AIDecisionQuestion question, AIDecisionAnswer answer)
        {
            var harness = await DecisionTrackingAndChecksHarness.CreateAsync(new FakeDecisionClient(_ => new AIDecisionResponse
            {
                Answers = new Dictionary<string, AIDecisionAnswer> { ["q"] = answer },
            }));

            await Should.ThrowAsync<AIProviderException>(() => harness.Client.GetResponseAsync(One(question)));

            harness.AuditLogServiceMock.Verify(
                x => x.QueueRecordAuditLogFailureAsync(
                    It.IsAny<AIAuditLog>(), It.IsAny<AIAuditPrompt?>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    #endregion
}
