// Builders for the agent-selection specs that compile today, without the T7-T10 types
// (IAIAgentSelectionService, AIAgentSelectionService, StreamAgentAGUIController changes).
// Everything here is unconditional - it exercises code that already exists on this branch.
// The gated AgentSelectionTestHarness.cs is for specs that need those future types; names here
// are kept distinct from it (AgentServiceAuditHarness vs. its AgentServiceHarness) so the two
// files won't collide once the harness's own #if comes down in a later task.
using Microsoft.Extensions.AI;
using Moq;
using Umbraco.AI.AGUI.Events;
using Umbraco.AI.AGUI.Models;
using Umbraco.AI.Agent.Core.AGUI;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Agent.Core.Chat;
using Umbraco.AI.Core.RuntimeContext;
using Umbraco.AI.Core.Tools;
using Umbraco.Cms.Core.Events;
using CoreConstants = Umbraco.AI.Core.Constants;
using MsAIAgent = Microsoft.Agents.AI.AIAgent;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Agents.Selection;

/// <summary>Builders shared by agent-selection specs that only need today's real types.</summary>
internal static class AgentSelectionTestBuilders
{
    /// <summary>A minimal active, standard agent - enough to drive <see cref="AIAgentService"/>.</summary>
    public static UmbracoAIAgent CreateAgent(Guid id, string alias)
    {
        var agent = new UmbracoAIAgent
        {
            Alias = alias,
            Name = $"{alias} name",
            AgentType = AIAgentType.Standard,
            IsActive = true,
            Config = new AIStandardAgentConfig
            {
                AllowedToolIds = [],
                AllowedToolScopeIds = [],
            },
        };
        agent.Id = id; // internal setter, visible to this test assembly
        return agent;
    }

    /// <summary>
    /// A real <see cref="AIAgentService"/>, built from today's constructor (no selection-service
    /// parameter - that lands in a later task), wired just far enough to run the AG-UI options
    /// overload of <see cref="AIAgentService.StreamAgentAGUIAsync(Guid, AGUIRunRequest, IEnumerable{AIFrontendTool}?, AIAgentExecutionOptions, CancellationToken)"/>
    /// and capture the additional properties handed to the agent factory.
    /// </summary>
    public sealed class AgentServiceAuditHarness
    {
        public AgentServiceAuditHarness(UmbracoAIAgent agent)
        {
            var repository = new Mock<IAIAgentRepository>();
            repository.Setup(x => x.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);

            var messageConverter = new Mock<IAGUIMessageConverter>();
            messageConverter.Setup(x => x.ConvertToChatMessages(It.IsAny<IEnumerable<AGUIMessage>?>())).Returns([]);

            var contextConverter = new Mock<IAGUIContextConverter>();
            contextConverter
                .Setup(x => x.ConvertToRequestContextItems(It.IsAny<IEnumerable<AGUIContextItem>>()))
                .Returns([]);

            var agentFactory = new Mock<IAIAgentFactory>();
            agentFactory
                .Setup(x => x.CreateAgentAsync(
                    It.IsAny<UmbracoAIAgent>(),
                    It.IsAny<IEnumerable<AIRequestContextItem>?>(),
                    It.IsAny<IEnumerable<AITool>?>(),
                    It.IsAny<IReadOnlyDictionary<string, object?>?>(),
                    It.IsAny<AIApprovalPolicy>(),
                    It.IsAny<CancellationToken>()))
                .Callback<UmbracoAIAgent, IEnumerable<AIRequestContextItem>?, IEnumerable<AITool>?, IReadOnlyDictionary<string, object?>?, AIApprovalPolicy, CancellationToken>(
                    (_, _, _, props, _, _) => AdditionalProperties = props)
                .ReturnsAsync(new Mock<MsAIAgent>().Object);

            var streamingService = new Mock<IAGUIStreamingService>();
            streamingService
                .Setup(x => x.StreamAgentAsync(
                    It.IsAny<MsAIAgent>(), It.IsAny<AGUIRunRequest>(), It.IsAny<IEnumerable<AITool>?>(), It.IsAny<CancellationToken>()))
                .Returns(EmptyEventStream());

            Service = new AIAgentService(
                repository.Object,
                null!, // IAIEntityVersionService
                agentFactory.Object,
                streamingService.Object,
                contextConverter.Object,
                messageConverter.Object,
                new AIToolCollection(() => []),
                null!, // IAIProfileService
                null!, // IAIGuardrailService
                null!, // IAIContextService
                null!, // IAIChatClientFactory
                null!, // AIAgentScopeValidator
                null!, // AIAgentSurfaceCollection
                Mock.Of<IEventAggregator>(),
                backOfficeSecurityAccessor: null);
        }

        public AIAgentService Service { get; }

        public IReadOnlyDictionary<string, object?>? AdditionalProperties { get; private set; }

        public IReadOnlyList<string> LogKeys
            => AdditionalProperties?.TryGetValue(CoreConstants.ContextKeys.LogKeys, out var keys) == true
                ? (string[])keys!
                : [];

        public async Task RunAsync(AIAgentExecutionOptions options, Guid agentId)
        {
            var request = new AGUIRunRequest { ThreadId = "thread", RunId = "run", Messages = [] };
            await foreach (var _ in Service.StreamAgentAGUIAsync(agentId, request, frontendTools: null, options))
            {
            }
        }
    }

    public static async IAsyncEnumerable<IAGUIEvent> EmptyEventStream()
    {
        await Task.CompletedTask;
        yield break;
    }
}
