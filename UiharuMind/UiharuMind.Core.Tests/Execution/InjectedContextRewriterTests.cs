using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools.Memory;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉住注入块改写器的三件事：<b>排在最后</b>、<b>只改该改的那条</b>、<b>删除工具有审批</b>。
///
/// 这三件都是"实机上几乎不可见"的那类：顺序错了改写就静默不生效；认错来源会把知识库片段
/// 或待办清单一起改掉；审批漏了要等用户的长期记忆真被删掉才发现。
/// </summary>
public class InjectedContextRewriterTests
{
    private static readonly string FileMemorySourceId = typeof(FileMemoryProvider).FullName!;

    /// <summary>
    /// 改写器靠"排在最后"才看得见前面 provider 的产出，所以顺序是契约。
    /// 曾经把它加在 <c>MemoryContextProvider</c> 之前是能编译过的，而表现只是"改写没生效"。
    /// </summary>
    [Fact]
    public void BuildContextProviders_RewriterIsLast()
    {
        List<AIContextProvider> providers = AgentAssembler.BuildContextProviders(BuildPlan());

        Assert.IsType<InjectedContextRewriter>(providers[^1]);
    }

    /// <summary>关掉文件记忆就没有可改写的东西，挂上去只是一次空遍历</summary>
    [Fact]
    public void BuildContextProviders_NoRewriterWhenFileMemoryOff()
    {
        List<AIContextProvider> providers = AgentAssembler.BuildContextProviders(
            BuildPlan(new AgentToolConfig { EnableFileMemory = false }));

        Assert.DoesNotContain(providers, provider => provider is InjectedContextRewriter);
    }

    /// <summary>非智能体档整个禁用了框架文件记忆（ADR 0003），那里也不该有改写器</summary>
    [Fact]
    public void BuildContextProviders_NoRewriterForPromptOnlyKinds()
    {
        List<AIContextProvider> providers = AgentAssembler.BuildContextProviders(
            BuildPlan(kind: ECharacterKind.Roleplay));

        Assert.DoesNotContain(providers, provider => provider is InjectedContextRewriter);
    }

    /// <summary>
    /// 索引块要换上带防御的引导句，而<b>正文一个字不能少</b>——引导句换错了只是没治好自言自语，
    /// 正文丢了则是记忆当场消失。
    /// </summary>
    [Fact]
    public void RewriteMessages_ReplacesFileMemoryHeaderAndKeepsBody()
    {
        const string body = "# Memory Index\n\n- **prefs.md**: user prefers concise answers\n";
        ChatMessage injected = Attribute(new ChatMessage(ChatRole.User,
            "The following is your memory index — a list of files you have previously written. " + body));

        ChatMessage result = InjectedContextRewriter.RewriteMessages([injected])[0];

        Assert.DoesNotContain("The following is your memory index", result.Text);
        Assert.Contains(InjectedBlockGuard.Rules, result.Text);
        Assert.Contains(body, result.Text);
    }

    /// <summary>
    /// 改写后必须仍带溯源标记，否则 <c>SessionChatHistoryProvider</c> 会把它当成真实对话落盘，
    /// 于是历史里每轮多一份陈旧索引并逐轮回灌。
    /// </summary>
    [Fact]
    public void RewriteMessages_KeepsAttributionSoItStaysOutOfHistory()
    {
        ChatMessage injected = Attribute(new ChatMessage(ChatRole.User, "lead-in # Memory Index\n- **a.md**"));

        ChatMessage result = InjectedContextRewriter.RewriteMessages([injected])[0];

        Assert.Equal(AgentRequestMessageSourceType.AIContextProvider,
            result.GetAgentRequestMessageSourceType());
        Assert.Equal(FileMemorySourceId, result.GetAgentRequestMessageSourceId());
    }

    /// <summary>
    /// 只认 <c>FileMemoryProvider</c> 这一个来源：真实用户消息、以及别的 provider 的注入块
    /// （知识库片段、待办清单）都必须原样通过。认错来源会把已经写好防御的块二次改写。
    /// </summary>
    [Fact]
    public void RewriteMessages_LeavesEveryOtherSourceUntouched()
    {
        ChatMessage user = new(ChatRole.User, "# Memory Index looks like this, right?");
        ChatMessage otherProvider = new ChatMessage(ChatRole.User, "# Memory Index\n- **a.md**")
            .WithAgentRequestMessageSource(AgentRequestMessageSourceType.AIContextProvider, "Some.Other.Provider");

        List<ChatMessage> result = InjectedContextRewriter.RewriteMessages([user, otherProvider]);

        Assert.Same(user, result[0]);
        Assert.Same(otherProvider, result[1]);
    }

    /// <summary>
    /// 删除工具必须包上审批：框架那七个工具一个都没包，而没包的函数会被
    /// <c>ApprovalNotRequiredFunctionBypassingChatClient</c> 直接旁路，
    /// 净效果是只读档也能静默删掉用户的跨会话长期记忆。
    /// </summary>
    [Fact]
    public void GateTools_WrapsDeleteOnly()
    {
        AITool delete = Function(FileMemoryProvider.DeleteFileToolName);
        AITool write = Function(FileMemoryProvider.WriteToolName);
        AITool read = Function(FileMemoryProvider.ReadFileToolName);

        List<AITool> result = InjectedContextRewriter.GateTools([delete, write, read]);

        Assert.IsType<ApprovalRequiredAIFunction>(result[0]);
        Assert.Same(write, result[1]);
        Assert.Same(read, result[2]);
    }

    /// <summary>已经包过的不再包一层：框架哪天自己包上了，双层审批会让用户点两次</summary>
    [Fact]
    public void GateTools_DoesNotDoubleWrap()
    {
        ApprovalRequiredAIFunction gated = new(Function(FileMemoryProvider.DeleteFileToolName));

        List<AITool> result = InjectedContextRewriter.GateTools([gated]);

        Assert.Same(gated, result[0]);
    }

    private static ChatMessage Attribute(ChatMessage message)
    {
        return message.WithAgentRequestMessageSource(
            AgentRequestMessageSourceType.AIContextProvider, FileMemorySourceId);
    }

    private static AIFunction Function(string name)
    {
        return AIFunctionFactory.Create(() => "ok", new AIFunctionFactoryOptions { Name = name });
    }

    private static AgentAssemblyPlan BuildPlan(AgentToolConfig? tools = null,
        ECharacterKind kind = ECharacterKind.Agent)
    {
        return new AgentAssemblyPlan
        {
            Profile = new AgentBuildProfile
            {
                Character = new CharacterData
                {
                    CharacterId = "agent", Kind = kind, Tools = tools ?? new AgentToolConfig(),
                },
                PermissionMode = EAgentPermissionMode.AutoEdit,
            },
        };
    }
}
