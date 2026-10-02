using Microsoft.Extensions.AI;
using Umbraco.AI.Core.Chat.Middleware;
using Umbraco.AI.Core.RuntimeContext;
using Umbraco.AI.Core.Tools;
using Umbraco.AI.Tests.Common.Fakes;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.AI.Tests.Unit.Middleware;

public class AIToolExecutionNotificationTests
{
    private const string CallId = "call-1";

    private readonly List<INotification> _published = [];
    private readonly Mock<IEventAggregator> _eventAggregatorMock = new();
    private readonly Mock<IAIRuntimeContextAccessor> _contextAccessorMock = new();
    private readonly AIRuntimeContext _runtimeContext = new([]);

    public AIToolExecutionNotificationTests()
    {
        _contextAccessorMock.Setup(x => x.Context).Returns(_runtimeContext);
        Capture<AIToolExecutingNotification>();
        Capture<AIToolExecutedNotification>();
    }

    [Fact]
    public async Task ToolCall_PublishesExecutingThenExecuted()
    {
        var tool = new FakeTool("my-tool").WithExecuteHandler((_, _) => Task.FromResult<object>("done"));

        await RunToolCallAsync(new AIToolFunction(tool, "my-tool", "desc"));

        _published.Count.ShouldBe(2);
        var executing = _published[0].ShouldBeOfType<AIToolExecutingNotification>();
        executing.ToolName.ShouldBe("my-tool");
        executing.CallId.ShouldBe(CallId);
        executing.Tool.ShouldBeSameAs(tool);
        executing.RuntimeContext.ShouldBeSameAs(_runtimeContext);

        var executed = _published[1].ShouldBeOfType<AIToolExecutedNotification>();
        executed.IsSuccess.ShouldBeTrue();
        executed.Result.ShouldBe("done");
        executed.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task ToolCall_ExecutingCancelled_DoesNotRunToolAndTellsModel()
    {
        var ran = false;
        var tool = new FakeTool("publish").WithExecuteHandler((_, _) =>
        {
            ran = true;
            return Task.FromResult<object>("published");
        });
        _eventAggregatorMock
            .Setup(x => x.PublishAsync(It.IsAny<AIToolExecutingNotification>(), It.IsAny<CancellationToken>()))
            .Callback<INotification, CancellationToken>((n, _) =>
            {
                _published.Add(n);
                var executing = (AIToolExecutingNotification)n;
                executing.Messages.Add(new EventMessage("Blocked", "Publishing is frozen today.", EventMessageType.Error));
                executing.Cancel = true;
            })
            .Returns(Task.CompletedTask);

        var inner = await RunToolCallAsync(new AIToolFunction(tool, "publish", "desc"));

        ran.ShouldBeFalse();
        _published.ShouldHaveSingleItem().ShouldBeOfType<AIToolExecutingNotification>();
        ToolResultSentToModel(inner).ShouldBe(
            "The 'publish' tool was blocked and was not run. Reason: Publishing is frozen today.");
    }

    [Fact]
    public async Task ToolCall_ToolThrows_PublishesFailedExecuted()
    {
        var tool = new FakeTool("broken").WithExecuteHandler((_, _) => throw new InvalidOperationException("boom"));

        await RunToolCallAsync(new AIToolFunction(tool, "broken", "desc"));

        var executed = _published.OfType<AIToolExecutedNotification>().ShouldHaveSingleItem();
        executed.IsSuccess.ShouldBeFalse();
        executed.Exception.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task ToolCall_FunctionHandsOffToClient_PublishesExecutingOnly()
    {
        var frontendTool = Microsoft.Extensions.AI.AIFunctionFactory.Create(
            () =>
            {
                FunctionInvokingChatClient.CurrentContext!.Terminate = true;
                return (object?)null;
            },
            "save_and_publish");

        await RunToolCallAsync(frontendTool);

        var executing = _published.ShouldHaveSingleItem().ShouldBeOfType<AIToolExecutingNotification>();
        executing.Tool.ShouldBeNull();
    }

    [Fact]
    public async Task ToolCall_NonExecutingFunction_PublishesNothing()
    {
        await RunToolCallAsync(new NonExecutingFunction());

        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task ToolCall_WrappedForApproval_StillFindsUmbracoTool()
    {
        var tool = new FakeTool("wrapped");
        var wrapped = new PassThroughFunction(new AIToolFunction(tool, "wrapped", "desc"));

        await RunToolCallAsync(wrapped);

        _published.OfType<AIToolExecutingNotification>().ShouldHaveSingleItem().Tool.ShouldBeSameAs(tool);
    }

    private async Task<FakeChatClient> RunToolCallAsync(AIFunction function)
    {
        var turn = 0;
        var inner = new FakeChatClient((_, _, _) => Task.FromResult(++turn == 1
            ? new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent(CallId, function.Name, new Dictionary<string, object?>())]))
            : new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))));

        var client = new AIFunctionInvokingChatMiddleware(_eventAggregatorMock.Object, _contextAccessorMock.Object)
            .Apply(inner);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "go")],
            new ChatOptions { Tools = [function] });

        return inner;
    }

    private static string? ToolResultSentToModel(FakeChatClient inner)
        => inner.ReceivedMessages.Last()
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Single(r => r.CallId == CallId)
            .Result?.ToString();

    private void Capture<TNotification>()
        where TNotification : INotification
        => _eventAggregatorMock
            .Setup(x => x.PublishAsync(It.IsAny<TNotification>(), It.IsAny<CancellationToken>()))
            .Callback<INotification, CancellationToken>((n, _) => _published.Add(n))
            .Returns(Task.CompletedTask);

    private sealed class NonExecutingFunction : AIFunction, IAINonExecutingFunction
    {
        public override string Name => "denied";

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            => ValueTask.FromResult<object?>("not run");
    }

    private sealed class PassThroughFunction(AIFunction inner) : DelegatingAIFunction(inner);
}
