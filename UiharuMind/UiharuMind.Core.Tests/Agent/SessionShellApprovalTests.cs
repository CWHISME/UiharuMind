using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Agent;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死会话级 shell 放行的两条语义：
/// ①模式派生只取命令前两词(取一词过宽会连 git push 一起放行)；
/// ②放行规则现取现用且只作用于 shell 工具,追加模式无需重建装配。
/// </summary>
public class SessionShellApprovalTests
{
    [Theory]
    [InlineData("git status --short", "git status*")]
    [InlineData("dotnet build -c Release", "dotnet build*")]
    [InlineData("ls", "ls*")]
    [InlineData("  git   log  ", "git log*")]
    [InlineData("", "")]
    public void DeriveCommandPattern_TakesFirstTwoTokens(string command, string expected)
    {
        Assert.Equal(expected, ApprovalModeMapper.DeriveCommandPattern(command));
    }

    [Fact]
    public async Task SessionRule_ApprovesMatchingShellCommand_LiveWithoutRebuild()
    {
        List<string> patterns = []; //模拟会话放行清单:规则构建后再追加
        var rules = ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit,
            sessionShellApprovalSource: () => patterns);

        FunctionCallContent shellCall = new("c1", AgentHost.ShellToolName,
            new Dictionary<string, object?> { ["command"] = "git status --short" });

        Assert.False(await EvaluateAsync(rules, shellCall)); //清单为空:仍需人工审批

        patterns.Add("git status*"); //用户点了"记住同类命令"
        Assert.True(await EvaluateAsync(rules, shellCall)); //同一规则集,立即生效

        FunctionCallContent pushCall = new("c2", AgentHost.ShellToolName,
            new Dictionary<string, object?> { ["command"] = "git push origin main" });
        Assert.False(await EvaluateAsync(rules, pushCall)); //前两词模式圈不住 push
    }

    [Fact]
    public async Task SessionRule_NeverApprovesNonShellTools()
    {
        var rules = ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit,
            sessionShellApprovalSource: () => ["*"]);

        FunctionCallContent writeCall = new("c1", "Write",
            new Dictionary<string, object?> { ["command"] = "anything" });

        Assert.False(await EvaluateAsync(rules, writeCall));
    }

    private static async Task<bool> EvaluateAsync(
        IEnumerable<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules, FunctionCallContent call)
    {
        // 1.16 起规则收上下文对象;用最小桩 agent/session 构造之
        ChatClientAgent agent = new(new NullChatClient());
        AgentSession session = await agent.CreateSessionAsync();
        ToolAutoApprovalRuleContext context = new(call, agent, session, [], null);
        foreach (var rule in rules)
        {
            if (await rule(context)) return true;
        }

        return false;
    }

    private sealed class NullChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse());
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
