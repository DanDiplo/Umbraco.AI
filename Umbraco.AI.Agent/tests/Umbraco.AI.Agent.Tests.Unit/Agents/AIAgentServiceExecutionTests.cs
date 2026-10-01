using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Moq;
using Shouldly;
using Umbraco.AI.AGUI.Events;
using Umbraco.AI.AGUI.Events.Lifecycle;
using Umbraco.AI.AGUI.Models;
using Umbraco.AI.Agent.Core.AGUI;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Agent.Core.Chat;
using Umbraco.AI.Core.RuntimeContext;
using Umbraco.AI.Core.Tools;
using Umbraco.Cms.Core.Events;
using Xunit;
using AIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;
using MsAIAgent = Microsoft.Agents.AI.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Agents;

public class AIAgentServiceExecutionTests
{
    private static readonly Guid TestAgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task RunAgentAsync_ForwardsAdditionalPropertiesToAgentFactory()
    {
        // Arrange
        var agent = CreateAgent(TestAgentId);
        var repositoryMock = new Mock<IAIAgentRepository>();
        repositoryMock
            .Setup(x => x.GetByIdAsync(TestAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        IReadOnlyDictionary<string, object?>? capturedAdditionalProperties = null;
        var agentFactoryMock = new Mock<IAIAgentFactory>();
        agentFactoryMock
            .Setup(x => x.CreateAgentAsync(
                agent,
                It.IsAny<IEnumerable<AIRequestContextItem>?>(),
                It.IsAny<IEnumerable<AITool>?>(),
                It.IsAny<IReadOnlyDictionary<string, object?>?>(),
                It.IsAny<AIApprovalPolicy>(),
                It.IsAny<CancellationToken>()))
            .Callback<AIAgent, IEnumerable<AIRequestContextItem>?, IEnumerable<AITool>?, IReadOnlyDictionary<string, object?>?, AIApprovalPolicy, CancellationToken>(
                (_, _, _, properties, _, _) => capturedAdditionalProperties = properties)
            .ReturnsAsync(CreateRespondingAgent());

        var eventAggregatorMock = new Mock<IEventAggregator>();
        eventAggregatorMock
            .Setup(x => x.PublishAsync(It.IsAny<AIAgentExecutingNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        eventAggregatorMock
            .Setup(x => x.PublishAsync(It.IsAny<AIAgentExecutedNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(repositoryMock.Object, agentFactoryMock.Object, eventAggregatorMock.Object);
        var additionalProperties = new Dictionary<string, object?>
        {
            ["key-a"] = "value-a",
            ["key-b"] = 42,
        };

        // Act
        await service.RunAgentAsync(
            TestAgentId,
            [new ChatMessage(ChatRole.User, "Hello")],
            new AIAgentExecutionOptions { AdditionalProperties = additionalProperties },
            CancellationToken.None);

        // Assert
        capturedAdditionalProperties.ShouldNotBeNull();
        capturedAdditionalProperties!["key-a"].ShouldBe("value-a");
        capturedAdditionalProperties["key-b"].ShouldBe(42);
    }

    [Fact]
    public async Task StreamAgentAGUIAsync_StreamEndsInRunError_PublishesFailedExecutedNotification()
    {
        // Arrange
        var executed = await StreamAndCaptureExecutedAsync(
            new RunStartedEvent { ThreadId = "t", RunId = "r" },
            new RunErrorEvent { Message = "The AI provider is unavailable.", Code = "Unavailable" });

        // Assert
        executed.IsSuccess.ShouldBeFalse();
        executed.Messages.GetAll().ShouldContain(m => m.Message == "The AI provider is unavailable.");
    }

    [Fact]
    public async Task StreamAgentAGUIAsync_StreamEndsInRunFinished_PublishesSuccessfulExecutedNotification()
    {
        // Arrange
        var executed = await StreamAndCaptureExecutedAsync(
            new RunStartedEvent { ThreadId = "t", RunId = "r" },
            new RunFinishedEvent { ThreadId = "t", RunId = "r", Outcome = new AGUIRunOutcomeSuccess() });

        // Assert
        executed.IsSuccess.ShouldBeTrue();
        executed.Messages.Count.ShouldBe(0);
    }

    private static async Task<AIAgentExecutedNotification> StreamAndCaptureExecutedAsync(params IAGUIEvent[] events)
    {
        var agent = CreateAgent(TestAgentId);
        var repositoryMock = new Mock<IAIAgentRepository>();
        repositoryMock
            .Setup(x => x.GetByIdAsync(TestAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        var agentFactoryMock = new Mock<IAIAgentFactory>();
        agentFactoryMock
            .Setup(x => x.CreateAgentAsync(
                agent,
                It.IsAny<IEnumerable<AIRequestContextItem>?>(),
                It.IsAny<IEnumerable<AITool>?>(),
                It.IsAny<IReadOnlyDictionary<string, object?>?>(),
                It.IsAny<AIApprovalPolicy>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRespondingAgent());

        AIAgentExecutedNotification? executed = null;
        var eventAggregatorMock = new Mock<IEventAggregator>();
        eventAggregatorMock
            .Setup(x => x.PublishAsync(It.IsAny<AIAgentExecutingNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        eventAggregatorMock
            .Setup(x => x.PublishAsync(It.IsAny<AIAgentExecutedNotification>(), It.IsAny<CancellationToken>()))
            .Callback<AIAgentExecutedNotification, CancellationToken>((n, _) => executed = n)
            .Returns(Task.CompletedTask);

        var streamingServiceMock = new Mock<IAGUIStreamingService>();
        streamingServiceMock
            .Setup(x => x.StreamAgentAsync(
                It.IsAny<MsAIAgent>(),
                It.IsAny<AGUIRunRequest>(),
                It.IsAny<IEnumerable<AITool>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(events.ToAsyncEnumerable());

        var messageConverterMock = new Mock<IAGUIMessageConverter>();
        messageConverterMock
            .Setup(x => x.ConvertToChatMessages(It.IsAny<IEnumerable<AGUIMessage>?>()))
            .Returns([new ChatMessage(ChatRole.User, "Hello")]);

        var contextConverterMock = new Mock<IAGUIContextConverter>();
        contextConverterMock
            .Setup(x => x.ConvertToRequestContextItems(It.IsAny<IEnumerable<AGUIContextItem>?>()))
            .Returns([]);

        var service = CreateService(
            repositoryMock.Object,
            agentFactoryMock.Object,
            eventAggregatorMock.Object,
            streamingServiceMock.Object,
            contextConverterMock.Object,
            messageConverterMock.Object);

        // Act
        await foreach (var _ in service.StreamAgentAGUIAsync(
            TestAgentId,
            new AGUIRunRequest { ThreadId = "t", RunId = "r" },
            frontendTools: null,
            CancellationToken.None))
        {
        }

        executed.ShouldNotBeNull();
        return executed!;
    }

    private static AIAgentService CreateService(
        IAIAgentRepository repository,
        IAIAgentFactory agentFactory,
        IEventAggregator eventAggregator,
        IAGUIStreamingService? streamingService = null,
        IAGUIContextConverter? contextConverter = null,
        IAGUIMessageConverter? messageConverter = null)
        => new(
            repository,
            null!, // IAIEntityVersionService
            agentFactory,
            streamingService!,
            contextConverter!,
            messageConverter!,
            new AIToolCollection(() => []),
            null!, // IAIProfileService
            null!, // IAIGuardrailService
            null!, // IAIContextService
            null!, // IAIChatClientFactory
            null!, // AIAgentScopeValidator
            null!, // AIAgentSurfaceCollection
            eventAggregator,
            null); // IBackOfficeSecurityAccessor

    private static AIAgent CreateAgent(Guid id)
        => new()
        {
            Id = id,
            Alias = "test-agent",
            Name = "Test Agent",
            AgentType = AIAgentType.Standard,
            Config = new AIStandardAgentConfig
            {
                AllowedToolIds = [],
                AllowedToolScopeIds = [],
            },
            IsActive = true,
        };

    private static MsAIAgent CreateRespondingAgent()
    {
        var chatClientMock = new Mock<IChatClient>();
        chatClientMock
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        return new ChatClientAgent(chatClientMock.Object);
    }
}
