// DR-10 — Copilot auto mode routes with Decision
//
// Re-targeted at DecisionAgentSelector now that agent selection moved to the pluggable
// IAIAgentSelector chain (umbraco/Umbraco.AI#463) - this used to live inside AIAgentService as
// TrySelectAgentViaDecisionAsync. The Decision gate checks IAIProfileService.HasDefaultProfileAsync
// rather than catching an exception from GetDefaultProfileAsync, and the whole Decision attempt
// (gate + ask) falls back to "no opinion" (null) on any non-cancellation exception, so a site that
// doesn't use Decision at all (e.g. a database error resolving the default profile) never breaks
// agent routing - the next selector in the chain decides instead.
#pragma warning disable UMBRACOAI_DECISION

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Umbraco.AI.Agent.Core.Agents.Selection;
using Umbraco.AI.Core.Decision;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Profiles;
using Umbraco.AI.Core.Settings;
using Xunit;
using static Umbraco.AI.Agent.Tests.Unit.Agents.Selection.AgentSelectionTestBuilders;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Agents.Selection;

public class DecisionAgentSelectorTests
{
    private const string UserMessage = "Help me improve this page's SEO";

    private static readonly UmbracoAIAgent AgentA =
        CreateAgent(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "agent-a", description: "Handles topic A");
    private static readonly UmbracoAIAgent AgentB =
        CreateAgent(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), "agent-b", description: "Handles topic B");
    private static readonly UmbracoAIAgent AgentC =
        CreateAgent(Guid.Parse("cccccccc-0000-0000-0000-000000000003"), "agent-c", description: "Handles topic C");

    // ---- Happy path --------------------------------------------------------------------------

    // AC - Decision picks a candidate
    public class GivenDecisionPicksCandidateB
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenDecisionPicksCandidateB()
        {
            var (selector, decisionService, _, _, _) = CreateSelector();
            SetupDecisionAnswer(decisionService, AgentB.Id.ToString("D"));

            _result = selector.SelectAgentAsync(CreateRequest([AgentA, AgentB, AgentC])).GetAwaiter().GetResult();
        }

        [Fact]
        public void SelectsAgentB() => _result!.Agent.ShouldBe(AgentB);

        [Fact]
        public void RecordsTheDecisionSelectorId() => _result!.SelectorId.ShouldBe(AIAgentSelectorIds.Decision);
    }

    // AC - The Decision question describes the candidates, and state is the last user message
    public class GivenMultipleCandidates
    {
        private readonly AIChoiceDecisionQuestion _sentQuestion;
        private readonly string? _sentState;

        public GivenMultipleCandidates()
        {
            var (selector, decisionService, _, _, _) = CreateSelector();
            AIChoiceDecisionQuestion? capturedQuestion = null;
            string? capturedState = null;
            decisionService
                .Setup(s => s.AskAsync(It.IsAny<Action<AIDecisionBuilder>>(), It.IsAny<AIChoiceDecisionQuestion>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback<Action<AIDecisionBuilder>, AIDecisionQuestion<AIChoiceDecisionAnswer>, string?, CancellationToken>((_, q, state, _) =>
                {
                    capturedQuestion = (AIChoiceDecisionQuestion)q;
                    capturedState = state;
                })
                .ReturnsAsync(BuildAnswer(AgentA.Id.ToString("D")));

            var request = CreateRequest(
                [AgentA, AgentB, AgentC],
                messages: [new ChatMessage(ChatRole.User, UserMessage)]);

            selector.SelectAgentAsync(request).GetAwaiter().GetResult();

            _sentQuestion = capturedQuestion!;
            _sentState = capturedState;
        }

        [Fact]
        public void OptionKeysAreAgentIds()
            => _sentQuestion.Options.Select(o => o.Key).ShouldBe([AgentA.Id.ToString("D"), AgentB.Id.ToString("D"), AgentC.Id.ToString("D")]);

        [Fact]
        public void OptionDescriptionContainsAgentName() => _sentQuestion.Options[1].Description!.ShouldContain(AgentB.Name);

        [Fact]
        public void OptionDescriptionContainsAgentDescription() => _sentQuestion.Options[1].Description!.ShouldContain(AgentB.Description!);

        [Fact]
        public void StateIsTheLastUserMessage() => _sentState.ShouldBe(UserMessage);
    }

    // ---- Sad path / edge: defers (null) to the next selector in the chain --------------------

    // AC - Flag off
    public class GivenTheDecisionFlagIsOff
    {
        private readonly AIAgentSelectionResult? _result;
        private readonly Mock<IAIDecisionService> _decisionService;

        public GivenTheDecisionFlagIsOff()
        {
            var (selector, decisionService, _, _, _) = CreateSelector(flagEnabled: false);
            _decisionService = decisionService;
            _result = selector.SelectAgentAsync(CreateRequest([AgentA, AgentB, AgentC])).GetAwaiter().GetResult();
        }

        [Fact]
        public void HasNoOpinion() => _result.ShouldBeNull();

        [Fact]
        public void MakesNoDecisionCall() => VerifyNoDecisionCall(_decisionService);
    }

    // AC - No default Decision profile
    public class GivenNoDefaultDecisionProfile
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenNoDefaultDecisionProfile()
        {
            var (selector, _, _, _, _) = CreateSelector(hasDefaultProfile: false);
            _result = selector.SelectAgentAsync(CreateRequest([AgentA, AgentB, AgentC])).GetAwaiter().GetResult();
        }

        [Fact]
        public void HasNoOpinion() => _result.ShouldBeNull();
    }

    // AC - The default-profile check itself throws (e.g. a database error)
    public class GivenTheDefaultProfileCheckThrows
    {
        private readonly AIAgentSelectionResult? _result;
        private readonly Mock<ILogger<DecisionAgentSelector>> _logger;

        public GivenTheDefaultProfileCheckThrows()
        {
            var (selector, _, profileService, _, logger) = CreateSelector();
            _logger = logger;
            profileService
                .Setup(x => x.HasDefaultProfileAsync(AICapability.Decision, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("Database timed out."));

            _result = selector.SelectAgentAsync(CreateRequest([AgentA, AgentB, AgentC])).GetAwaiter().GetResult();
        }

        [Fact]
        public void HasNoOpinion() => _result.ShouldBeNull();

        [Fact]
        public void LogsAWarning() => VerifyWarningLogged(_logger);
    }

    // AC - Decision itself throws
    public class GivenDecisionThrows
    {
        private readonly AIAgentSelectionResult? _result;
        private readonly Mock<ILogger<DecisionAgentSelector>> _logger;

        public GivenDecisionThrows()
        {
            var (selector, decisionService, _, _, logger) = CreateSelector();
            _logger = logger;
            decisionService
                .Setup(s => s.AskAsync(It.IsAny<Action<AIDecisionBuilder>>(), It.IsAny<AIChoiceDecisionQuestion>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("TypeSafe is down."));

            _result = selector.SelectAgentAsync(CreateRequest([AgentA, AgentB, AgentC])).GetAwaiter().GetResult();
        }

        [Fact]
        public void HasNoOpinion() => _result.ShouldBeNull();

        [Fact]
        public void LogsAWarning() => VerifyWarningLogged(_logger);
    }

    // AC - Decision answers with a key that isn't one of the candidates
    public class GivenDecisionReturnsAnUnknownKey
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenDecisionReturnsAnUnknownKey()
        {
            var (selector, decisionService, _, _, _) = CreateSelector();
            SetupDecisionAnswer(decisionService, Guid.NewGuid().ToString("D"));

            _result = selector.SelectAgentAsync(CreateRequest([AgentA, AgentB, AgentC])).GetAwaiter().GetResult();
        }

        [Fact]
        public void HasNoOpinion() => _result.ShouldBeNull();
    }

    // AC - More than 255 candidates (AIChoiceDecisionQuestion.Options only supports 2-255 entries)
    public class GivenMoreThanTwoHundredAndFiftyFiveCandidates
    {
        private readonly AIAgentSelectionResult? _result;
        private readonly Mock<IAIDecisionService> _decisionService;

        public GivenMoreThanTwoHundredAndFiftyFiveCandidates()
        {
            var (selector, decisionService, _, _, _) = CreateSelector();
            _decisionService = decisionService;
            _result = selector.SelectAgentAsync(CreateRequest(CreateManyAgents(256))).GetAwaiter().GetResult();
        }

        [Fact]
        public void HasNoOpinion() => _result.ShouldBeNull();

        [Fact]
        public void MakesNoDecisionCall() => VerifyNoDecisionCall(_decisionService);
    }

    // ---- Sad path: cancellation is never treated as "no opinion" ----------------------------

    public class GivenTheDefaultProfileCheckThrowsCancellation
    {
        private readonly DecisionAgentSelector _selector;

        public GivenTheDefaultProfileCheckThrowsCancellation()
        {
            var (selector, _, profileService, _, _) = CreateSelector();
            _selector = selector;
            profileService
                .Setup(x => x.HasDefaultProfileAsync(AICapability.Decision, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
        }

        [Fact]
        public async Task Propagates()
            => await Should.ThrowAsync<OperationCanceledException>(
                () => _selector.SelectAgentAsync(CreateRequest([AgentA, AgentB, AgentC])));
    }

    public class GivenDecisionThrowsCancellation
    {
        private readonly DecisionAgentSelector _selector;

        public GivenDecisionThrowsCancellation()
        {
            var (selector, decisionService, _, _, _) = CreateSelector();
            _selector = selector;
            decisionService
                .Setup(s => s.AskAsync(It.IsAny<Action<AIDecisionBuilder>>(), It.IsAny<AIChoiceDecisionQuestion>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
        }

        [Fact]
        public async Task Propagates()
            => await Should.ThrowAsync<OperationCanceledException>(
                () => _selector.SelectAgentAsync(CreateRequest([AgentA, AgentB, AgentC])));
    }

    // ---- Through the real selection service: default chain order ----------------------------

    // AC - Decision registered before LLM: Decision wins when it answers, LLM runs when it doesn't
    public class GivenDecisionAndLLMBothRegistered
    {
        [Fact]
        public void DecisionWinsWhenItAnswers()
        {
            var (decisionSelector, decisionService, _, _, _) = CreateSelector();
            SetupDecisionAnswer(decisionService, AgentB.Id.ToString("D"));
            var (llmSelector, llmPrompts) = CreateLLMSelector(AgentA.Id.ToString("D"));

            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB)
                .WithSelectors(decisionSelector, llmSelector)
                .Build();

            var result = service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();

            result!.Agent.ShouldBe(AgentB);
            result.SelectorId.ShouldBe(AIAgentSelectorIds.Decision);
            llmPrompts.ShouldBeEmpty();
        }

        [Fact]
        public void LLMRunsWhenDecisionHasNoOpinion()
        {
            var (decisionSelector, _, _, _, _) = CreateSelector(flagEnabled: false);
            var (llmSelector, _) = CreateLLMSelector(AgentB.Id.ToString("D"));

            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB)
                .WithSelectors(decisionSelector, llmSelector)
                .Build();

            var result = service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();

            result!.Agent.ShouldBe(AgentB);
            result.SelectorId.ShouldBe(AIAgentSelectorIds.Llm);
        }
    }

    // ---- Shared helpers -----------------------------------------------------------------------

    private static (
        DecisionAgentSelector Selector,
        Mock<IAIDecisionService> DecisionService,
        Mock<IAIProfileService> ProfileService,
        Mock<IAIExperimentalFeatures> ExperimentalFeatures,
        Mock<ILogger<DecisionAgentSelector>> Logger) CreateSelector(bool flagEnabled = true, bool hasDefaultProfile = true)
    {
        var decisionService = new Mock<IAIDecisionService>();
        var profileService = new Mock<IAIProfileService>();
        var experimentalFeatures = new Mock<IAIExperimentalFeatures>();
        var logger = new Mock<ILogger<DecisionAgentSelector>>();

        experimentalFeatures.Setup(x => x.IsCapabilityEnabled(AICapability.Decision)).Returns(flagEnabled);
        profileService
            .Setup(x => x.HasDefaultProfileAsync(AICapability.Decision, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasDefaultProfile);

        var selector = new DecisionAgentSelector(decisionService.Object, profileService.Object, experimentalFeatures.Object, logger.Object);
        return (selector, decisionService, profileService, experimentalFeatures, logger);
    }

    private static void SetupDecisionAnswer(Mock<IAIDecisionService> decisionService, string key)
        => decisionService
            .Setup(s => s.AskAsync(It.IsAny<Action<AIDecisionBuilder>>(), It.IsAny<AIChoiceDecisionQuestion>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildAnswer(key));

    private static AIDecisionResponse<AIChoiceDecisionAnswer> BuildAnswer(string key) => new()
    {
        Answer = new AIChoiceDecisionAnswer { Choice = key, Probabilities = new Dictionary<string, double> { [key] = 0.9 } },
        Answers = new Dictionary<string, AIDecisionAnswer>(),
    };

    private static void VerifyNoDecisionCall(Mock<IAIDecisionService> decisionService)
        => decisionService.Verify(
            s => s.AskAsync(It.IsAny<Action<AIDecisionBuilder>>(), It.IsAny<AIChoiceDecisionQuestion>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private static void VerifyWarningLogged(Mock<ILogger<DecisionAgentSelector>> logger)
        => logger.Verify(
            l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

    private static List<UmbracoAIAgent> CreateManyAgents(int count)
    {
        var agents = new List<UmbracoAIAgent>();
        for (var i = 0; i < count; i++)
        {
            agents.Add(CreateAgent(Guid.NewGuid(), $"many-agent-{i}"));
        }

        return agents;
    }
}
