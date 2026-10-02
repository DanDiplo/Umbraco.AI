// DR-16 — Ask several questions in one Automate step (AC5, AC6, AC8, AC9 run-time guard).
//
// ASSUMPTIONS (T37 builder confirms/adjusts, keeping each test's behavior and single assertion):
// - AskDecisionsAction : DynamicOutputActionBase<AskDecisionsSettings>, constructed like the other
//   decision actions: (ActionInfrastructure, IAIDecisionService, IAIExperimentalFeatures, ILogger<T>).
// - AskDecisionsSettings { Guid? ProfileId; string? Context; List<AskDecisionsQuestion> Questions }.
// - AskDecisionsQuestion is the flat shape from ARCHITECTURE decision 6: Kind ("binary" | "choice" |
//   "score", matching the editor's `kind`), Alias, Instructions, TrueCriteria, FalseCriteria,
//   Threshold, Options (List<AskChoiceDecisionOption>), Levels (List<string>).
// - The action calls IAIDecisionService.GetDecisionResponseAsync(Action<AIDecisionBuilder>,
//   AIDecisionRequest, CancellationToken), with each question's Id = its Alias.
// - OutputData serializes (web defaults) to one object per alias with camelCase fields.
// - Constant UmbracoAIAutomateConstants.ActionTypes.AskDecisions = "umbracoAI.askDecisions".
// Flag off at startup (action excluded from the picker) is proven by wire task T39, as for the
// other three actions.
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Umbraco.AI.Automate.Actions;
using Umbraco.AI.Core.Decision;
using Umbraco.AI.Core.Settings;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Settings;
using Umbraco.Automate.Core.StepTypes;
using Xunit;

#pragma warning disable UMBRACOAI_DECISION

namespace Umbraco.AI.Automate.Tests.Unit.Actions;

public class AskDecisionsActionTests
{
    private readonly Mock<IAIDecisionService> _decisionServiceMock = new();
    private readonly Mock<IAIExperimentalFeatures> _experimentalMock = new();
    private readonly ActionInfrastructure _infrastructure = new(new Mock<IEditableModelResolver>().Object);

    public AskDecisionsActionTests()
    {
        _experimentalMock.Setup(x => x.IsCapabilityEnabled(It.IsAny<Umbraco.AI.Core.Models.AICapability>())).Returns(true);
        _decisionServiceMock
            .Setup(s => s.GetDecisionResponseAsync(It.IsAny<Action<AIDecisionBuilder>>(), It.IsAny<AIDecisionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIDecisionResponse
            {
                Answers = new Dictionary<string, AIDecisionAnswer>
                {
                    ["refund"] = new AIBinaryDecisionAnswer { TrueProbability = 0.97 },
                    ["category"] = new AIChoiceDecisionAnswer
                    {
                        Choice = "billing",
                        Confidence = 0.8,
                        Probabilities = new Dictionary<string, double> { ["billing"] = 0.8, ["shipping"] = 0.2 },
                    },
                    ["mood"] = new AIScoreDecisionAnswer
                    {
                        Score = 1.6,
                        Confidence = 0.7,
                        Probabilities = new Dictionary<int, double> { [0] = 0.1, [1] = 0.2, [2] = 0.7 },
                    },
                },
            });
    }

    private static List<AskDecisionsQuestion> ThreeQuestions() =>
    [
        new() { Kind = "binary", Alias = "refund", Instructions = "Refund requested?", Threshold = 0.5 },
        new()
        {
            Kind = "choice",
            Alias = "category",
            Instructions = "What about?",
            Options = [new AskChoiceDecisionOption { Key = "billing" }, new AskChoiceDecisionOption { Key = "shipping" }],
        },
        new() { Kind = "score", Alias = "mood", Instructions = "How frustrated?", Levels = ["calm", "concerned", "angry"] },
    ];

    #region Scenario: three questions run in one step

    [Fact(Skip = "Pending T37")]
    public async Task MakesExactlyOneDecisionCall()
    {
        await RunAsync(new AskDecisionsSettings { Context = "My order", Questions = ThreeQuestions() });

        _decisionServiceMock.Verify(
            s => s.GetDecisionResponseAsync(It.IsAny<Action<AIDecisionBuilder>>(), It.IsAny<AIDecisionRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(Skip = "Pending T37")]
    public async Task SendsContextAsState()
    {
        AIDecisionRequest? sent = null;
        _decisionServiceMock
            .Setup(s => s.GetDecisionResponseAsync(It.IsAny<Action<AIDecisionBuilder>>(), It.IsAny<AIDecisionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Action<AIDecisionBuilder>, AIDecisionRequest, CancellationToken>((_, r, _) => sent = r)
            .ReturnsAsync(new AIDecisionResponse { Answers = new Dictionary<string, AIDecisionAnswer>() });

        await RunAsync(new AskDecisionsSettings { Context = "My order", Questions = ThreeQuestions() });

        sent!.State.ShouldBe("My order");
    }

    [Fact(Skip = "Pending T37")]
    public async Task OutputsTheYesNoAnswer()
        => (await OutputAsync()).GetProperty("refund").GetProperty("answer").GetBoolean().ShouldBeTrue();

    [Fact(Skip = "Pending T37")]
    public async Task OutputsTheYesNoProbability()
        => (await OutputAsync()).GetProperty("refund").GetProperty("probability").GetDouble().ShouldBe(0.97);

    [Fact(Skip = "Pending T37")]
    public async Task OutputsTheChoice()
        => (await OutputAsync()).GetProperty("category").GetProperty("choice").GetString().ShouldBe("billing");

    [Fact(Skip = "Pending T37")]
    public async Task OutputsTheChoiceConfidence()
        => (await OutputAsync()).GetProperty("category").GetProperty("confidence").GetDouble().ShouldBe(0.8);

    [Fact(Skip = "Pending T37")]
    public async Task OutputsTheScore()
        => (await OutputAsync()).GetProperty("mood").GetProperty("score").GetDouble().ShouldBe(1.6);

    [Fact(Skip = "Pending T37")]
    public async Task OutputsTheNearestLevelLabel()
        => (await OutputAsync()).GetProperty("mood").GetProperty("level").GetString().ShouldBe("angry");

    [Fact(Skip = "Pending T37")]
    public async Task OutputsTheScoreConfidence()
        => (await OutputAsync()).GetProperty("mood").GetProperty("confidence").GetDouble().ShouldBe(0.7);

    #endregion

    #region Scenario: the output schema follows the configured questions

    [Fact(Skip = "Pending T37")]
    public async Task OutputSchemaListsEachAlias()
    {
        var schema = await SchemaAsync();

        schema!.GetProperties()!.Keys.ShouldBe(["refund", "category", "mood"], ignoreOrder: true);
    }

    [Fact(Skip = "Pending T37")]
    public async Task OutputSchemaDescribesYesNoFields()
        => (await SchemaAsync())!.GetProperties()!["refund"].GetProperties()!.Keys.ShouldBe(["answer", "probability"], ignoreOrder: true);

    [Fact(Skip = "Pending T37")]
    public async Task OutputSchemaDescribesPickOneFields()
        => (await SchemaAsync())!.GetProperties()!["category"].GetProperties()!.Keys.ShouldBe(["choice", "confidence"], ignoreOrder: true);

    [Fact(Skip = "Pending T37")]
    public async Task OutputSchemaDescribesScoreFields()
        => (await SchemaAsync())!.GetProperties()!["mood"].GetProperties()!.Keys.ShouldBe(["score", "level", "confidence"], ignoreOrder: true);

    #endregion

    #region Sad path: invalid settings

    public static TheoryData<string, List<AskDecisionsQuestion>> InvalidQuestionSets => new()
    {
        { "no questions", [] },
        {
            "more than 20 questions",
            Enumerable.Range(0, 21).Select(i => new AskDecisionsQuestion { Kind = "binary", Alias = $"q{i}", Instructions = "?" }).ToList()
        },
        {
            "a duplicate alias",
            [
                new() { Kind = "binary", Alias = "same", Instructions = "One?" },
                new() { Kind = "binary", Alias = "same", Instructions = "Two?" },
            ]
        },
        {
            "a pick-one with one option",
            [new() { Kind = "choice", Alias = "pick", Instructions = "Which?", Options = [new AskChoiceDecisionOption { Key = "a" }] }]
        },
    };

    [Theory(Skip = "Pending T37")]
    [MemberData(nameof(InvalidQuestionSets))]
    public async Task InvalidSettings_FailWithValidation(string _, List<AskDecisionsQuestion> questions)
    {
        var result = await RunAsync(new AskDecisionsSettings { Context = "text", Questions = questions });

        result.ErrorCategory.ShouldBe(StepRunErrorCategory.Validation);
    }

    [Theory(Skip = "Pending T37")]
    [MemberData(nameof(InvalidQuestionSets))]
    public async Task InvalidSettings_DoNotCallTheProvider(string _, List<AskDecisionsQuestion> questions)
    {
        await RunAsync(new AskDecisionsSettings { Context = "text", Questions = questions });

        _decisionServiceMock.Verify(
            s => s.GetDecisionResponseAsync(It.IsAny<Action<AIDecisionBuilder>>(), It.IsAny<AIDecisionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Sad path: flag turned off after startup

    [Fact(Skip = "Pending T37")]
    public async Task WhenFlagOff_FailsWithValidation()
    {
        _experimentalMock.Setup(x => x.IsCapabilityEnabled(It.IsAny<Umbraco.AI.Core.Models.AICapability>())).Returns(false);

        var result = await RunAsync(new AskDecisionsSettings { Context = "text", Questions = ThreeQuestions() });

        result.ErrorCategory.ShouldBe(StepRunErrorCategory.Validation);
    }

    #endregion

    private AskDecisionsAction CreateAction()
        => new(_infrastructure, _decisionServiceMock.Object, _experimentalMock.Object, Mock.Of<ILogger<AskDecisionsAction>>());

    private Task<ActionResult> RunAsync(AskDecisionsSettings settings)
        => CreateAction().ExecuteAsync(
            new ActionContext
            {
                AutomationId = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                StepId = Guid.NewGuid(),
                ActionAlias = UmbracoAIAutomateConstants.ActionTypes.AskDecisions,
                Settings = settings,
            },
            CancellationToken.None);

    private async Task<JsonElement> OutputAsync()
    {
        var result = await RunAsync(new AskDecisionsSettings { Context = "My order", Questions = ThreeQuestions() });
        return JsonSerializer.SerializeToElement(result.OutputData, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    /// <summary>Through <see cref="IStepType"/>, as Automate's catalogue resolves it (see RunScriptActionTests).</summary>
    private Task<Json.Schema.JsonSchema?> SchemaAsync()
    {
        IStepType action = CreateAction();
        var settings = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(new AskDecisionsSettings { Questions = ThreeQuestions() }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return action.GetOutputSchemaAsync(settings);
    }
}
