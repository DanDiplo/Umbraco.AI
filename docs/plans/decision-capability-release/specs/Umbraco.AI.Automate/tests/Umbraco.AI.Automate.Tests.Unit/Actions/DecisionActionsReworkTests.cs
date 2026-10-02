// DR-9 — Branch automations on a decision (AC1, AC1b, AC3b, AC8): the reworked single-question actions.
//
// ASSUMPTIONS (T35 builder confirms/adjusts, keeping each test's behavior and single assertion):
// - The actions call IAIDecisionService.AskAsync<TAnswer>(Action<AIDecisionBuilder>,
//   AIDecisionQuestion<TAnswer>, string? state, CancellationToken) and get AIDecisionResponse<TAnswer>.
// - AskYesNoDecisionSettings gains Threshold (double, default 0.5); AskYesNoDecisionOutput loses Confidence.
// Supersedes the yes/no output scenario in DecisionActionsTests that asserts Confidence.
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Umbraco.AI.Automate.Actions;
using Umbraco.AI.Core.Decision;
using Umbraco.AI.Core.Settings;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Settings;
using Xunit;

#pragma warning disable UMBRACOAI_DECISION

namespace Umbraco.AI.Automate.Tests.Unit.Actions;

public class DecisionActionsReworkTests
{
    private readonly Mock<IAIDecisionService> _decisionServiceMock = new();
    private readonly Mock<IAIExperimentalFeatures> _experimentalMock = new();
    private readonly ActionInfrastructure _infrastructure = new(new Mock<IEditableModelResolver>().Object);

    public DecisionActionsReworkTests()
    {
        _experimentalMock.Setup(x => x.IsCapabilityEnabled(It.IsAny<Umbraco.AI.Core.Models.AICapability>())).Returns(true);
    }

    #region Scenario: "Ask yes/no" and the provider answers true-probability 0.9

    [Fact(Skip = "Pending T35")]
    public async Task AskYesNo_OutputsAnswerTrueAtTheDefaultThreshold()
    {
        SetupBinary(0.9);

        var result = await CreateYesNo().ExecuteAsync(YesNoContext(new AskYesNoDecisionSettings { Instructions = "Is this spam?" }), CancellationToken.None);

        result.OutputData.ShouldBeOfType<AskYesNoDecisionOutput>().Answer.ShouldBeTrue();
    }

    [Fact(Skip = "Pending T35")]
    public async Task AskYesNo_OutputsTheProbability()
    {
        SetupBinary(0.9);

        var result = await CreateYesNo().ExecuteAsync(YesNoContext(new AskYesNoDecisionSettings { Instructions = "Is this spam?" }), CancellationToken.None);

        result.OutputData.ShouldBeOfType<AskYesNoDecisionOutput>().Probability.ShouldBe(0.9);
    }

    [Fact(Skip = "Pending T35")]
    public void AskYesNoOutput_HasNoConfidence()
        => typeof(AskYesNoDecisionOutput).GetProperty("Confidence").ShouldBeNull();

    #endregion

    #region Scenario: "Ask yes/no" with Threshold 0.95 and the provider answers 0.9

    [Fact(Skip = "Pending T35")]
    public async Task AskYesNo_AboveTheProbability_OutputsAnswerFalse()
    {
        SetupBinary(0.9);

        var result = await CreateYesNo().ExecuteAsync(
            YesNoContext(new AskYesNoDecisionSettings { Instructions = "Is this spam?", Threshold = 0.95 }), CancellationToken.None);

        result.OutputData.ShouldBeOfType<AskYesNoDecisionOutput>().Answer.ShouldBeFalse();
    }

    #endregion

    #region Scenario: Context is sent as the request's state

    [Fact(Skip = "Pending T35")]
    public async Task AskYesNo_SendsContextAsState()
    {
        string? sentState = null;
        _decisionServiceMock
            .Setup(s => s.AskAsync(It.IsAny<Action<AIDecisionBuilder>>(), It.IsAny<AIBinaryDecisionQuestion>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Action<AIDecisionBuilder>, AIDecisionQuestion<AIBinaryDecisionAnswer>, string?, CancellationToken>((_, _, state, _) => sentState = state)
            .ReturnsAsync(Binary(0.9));

        await CreateYesNo().ExecuteAsync(
            YesNoContext(new AskYesNoDecisionSettings { Instructions = "Is this spam?", Context = "some text" }), CancellationToken.None);

        sentState.ShouldBe("some text");
    }

    #endregion

    #region Sad path: "Ask yes/no" with Threshold 1.5

    [Fact(Skip = "Pending T35")]
    public async Task AskYesNo_WithThresholdOutOfRange_FailsWithValidation()
    {
        SetupBinary(0.9);

        var result = await CreateYesNo().ExecuteAsync(
            YesNoContext(new AskYesNoDecisionSettings { Instructions = "Is this spam?", Threshold = 1.5 }), CancellationToken.None);

        result.ErrorCategory.ShouldBe(StepRunErrorCategory.Validation);
    }

    [Fact(Skip = "Pending T35")]
    public async Task AskYesNo_WithThresholdOutOfRange_DoesNotCallProvider()
    {
        SetupBinary(0.9);

        await CreateYesNo().ExecuteAsync(
            YesNoContext(new AskYesNoDecisionSettings { Instructions = "Is this spam?", Threshold = 1.5 }), CancellationToken.None);

        _decisionServiceMock.Verify(
            s => s.AskAsync(It.IsAny<Action<AIDecisionBuilder>>(), It.IsAny<AIBinaryDecisionQuestion>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    private static AIDecisionResponse<AIBinaryDecisionAnswer> Binary(double trueProbability)
    {
        var answer = new AIBinaryDecisionAnswer { TrueProbability = trueProbability };
        return new AIDecisionResponse<AIBinaryDecisionAnswer>
        {
            Answer = answer,
            Answers = new Dictionary<string, AIDecisionAnswer> { ["q"] = answer },
        };
    }

    private void SetupBinary(double trueProbability)
        => _decisionServiceMock
            .Setup(s => s.AskAsync(It.IsAny<Action<AIDecisionBuilder>>(), It.IsAny<AIBinaryDecisionQuestion>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Binary(trueProbability));

    private AskYesNoDecisionAction CreateYesNo()
        => new(_infrastructure, _decisionServiceMock.Object, _experimentalMock.Object, Mock.Of<ILogger<AskYesNoDecisionAction>>());

    private static ActionContext YesNoContext(AskYesNoDecisionSettings settings)
        => new()
        {
            AutomationId = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            StepId = Guid.NewGuid(),
            ActionAlias = UmbracoAIAutomateConstants.ActionTypes.AskYesNoDecision,
            Settings = settings,
        };
}
