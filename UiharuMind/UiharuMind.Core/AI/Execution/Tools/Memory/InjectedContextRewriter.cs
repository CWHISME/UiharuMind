/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Tools.Memory;

/// <summary>
/// 改写框架各 provider 注入的上下文块与工具，收口在<b>一个后置 provider</b> 里。
///
/// 之所以能这么做：框架把调用方给的 <c>AIContextProviders</c> 追加在自己那批之后
/// （<c>HarnessAgent.BuildContextProviders</c>），而 provider 按列表顺序串行执行、
/// 每个都收到累积后的 <see cref="AIContext"/>。因此排在最后的这一个能看见——并且能改——
/// 前面那些 provider 的产出。<b>所以本类必须是 <c>contextProviders</c> 的最后一项</b>
/// （有不变量测试钉住）。
///
/// 必须重写 <see cref="InvokingCoreAsync"/> 而不是 <c>ProvideAIContextAsync</c>：
/// 后者拿到的输入被基类滤成只剩本轮外部消息（<c>DefaultExternalOnlyFilter</c>），
/// 看不到别的 provider 的产出。这与 <see cref="MemoryContextProvider"/> 那条
/// 「绝不能回传 <c>context.AIContext</c>」的纪律不冲突：那条只约束
/// <c>ProvideAIContextAsync</c>（基类会把返回值再追加一遍），而本方法的契约恰恰是
/// <b>返回本轮完整的合并结果</b>。
///
/// 目前治两件事，都出在框架的 <c>FileMemoryProvider</c> 上：
/// <list type="number">
/// <item><description><b>记忆索引块的措辞</b>——见 <see cref="RewriteFileMemoryIndex"/>。</description></item>
/// <item><description><b>删除工具没有审批</b>——见 <see cref="GateTools"/>。</description></item>
/// </list>
/// Todo 与 mode 两个 provider 是同一套注入套路，暂不纳入：待办清单每轮变化且模型确实需要
/// 每轮看见，与记忆索引不是同一种语义。故规则做成按来源分发，加一档只是加一个分支。
/// </summary>
internal sealed class InjectedContextRewriter : AIContextProvider
{
    /// <summary>框架 provider 的类型全名，即它盖在注入消息上的来源标识</summary>
    private static readonly string FileMemorySourceId = typeof(FileMemoryProvider).FullName!;

    /// <summary>
    /// 索引正文的首行。框架 <c>RebuildMemoryIndexAsync</c> 写死以此开头，
    /// 用它把硬编码的引导句与正文切开；切不开就整段留着（宁可措辞旧，也不能把索引弄丢）。
    /// </summary>
    private const string IndexBodyMarker = "# Memory Index";

    /// <summary>
    /// 换给记忆索引的引导句。与框架那句的差别只有两点，但都是要紧的：
    /// 它明说这不是用户说的话，且明令不得在回复里提及。
    /// </summary>
    private static readonly string FileMemoryHeader =
        $"""
         [Memory Index]
         An index of the memory files you wrote in earlier sessions, listed by name and description.
         Read any of them with the file_memory_read tool when it is relevant to the current task.
         {InjectedBlockGuard.Rules}

         ---

         """;

    public override IReadOnlyList<string> StateKeys => [];

    protected override ValueTask<AIContext> InvokingCoreAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        AIContext input = context.AIContext;

        // 就地返回改写后的完整上下文:本 provider 自己不产出任何东西,只改别人的
        return new ValueTask<AIContext>(new AIContext
        {
            Instructions = input.Instructions,
            Messages = input.Messages == null ? null : RewriteMessages(input.Messages),
            Tools = input.Tools == null ? null : GateTools(input.Tools),
        });
    }

    /// <summary>
    /// 按来源分发改写。命中不了任何规则的消息原样返回（含引用同一实例），
    /// 因此绝大多数轮次这里只是一次遍历。
    /// </summary>
    internal static List<ChatMessage> RewriteMessages(IEnumerable<ChatMessage> messages)
    {
        // 物化而不是惰性 Select:返回值会被下游多次枚举,惰性会把改写重复算一遍
        List<ChatMessage> result = new();
        foreach (ChatMessage message in messages)
        {
            result.Add(Rewrite(message));
        }

        return result;
    }

    private static ChatMessage Rewrite(ChatMessage message)
    {
        if (message.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.AIContextProvider)
            return message;

        return message.GetAgentRequestMessageSourceId() == FileMemorySourceId
            ? RewriteFileMemoryIndex(message)
            : message;
    }

    /// <summary>
    /// 把记忆索引块的引导句换成带防御的那一版。
    ///
    /// 框架那条消息是 <c>ChatRole.User</c> 加一句无防御的引导（措辞硬编码在
    /// <c>FileMemoryProvider.ProvideAIContextAsync</c> 里，<c>FileMemoryProviderOptions</c>
    /// 只暴露系统提示那段，改不到这里），而它是本轮<b>最后一条用户消息</b>——
    /// 模型于是照着回「已收到记忆索引，稍后整理」。
    ///
    /// <b>角色仍保留 User</b>：换成 System 在本地模型上会出事——llama.cpp 一侧的 chat template
    /// 大多只认开头一条 system，对话中段的第二条轻则被静默丢弃、重则撑坏模板结构，
    /// 净效果是「远程有记忆、本地没记忆」这类最难自查的分裂行为。
    /// </summary>
    /// <param name="message">框架注入的那条消息</param>
    /// <returns>换过措辞的消息；正文切不出来时原样返回</returns>
    private static ChatMessage RewriteFileMemoryIndex(ChatMessage message)
    {
        string text = message.Text;
        int bodyStart = text.IndexOf(IndexBodyMarker, StringComparison.Ordinal);
        if (bodyStart < 0) return message;

        ChatMessage rewritten = new(message.Role, FileMemoryHeader + text[bodyStart..])
        {
            CreatedAt = message.CreatedAt,
        };

        // 溯源标记要一并带上,否则这条消息会被 SessionChatHistoryProvider 当成真实对话落盘,
        // 于是历史里每轮多一份陈旧索引
        return rewritten.WithAgentRequestMessageSource(
            AgentRequestMessageSourceType.AIContextProvider, FileMemorySourceId);
    }

    /// <summary>
    /// 给 <c>file_memory_delete</c> 补上审批。
    ///
    /// 框架的 <c>FileMemoryProvider.CreateTools</c> 用的是裸 <c>AIFunctionFactory.Create</c>，
    /// 一个都没包 <see cref="ApprovalRequiredAIFunction"/>（对照 <c>FileAccessProvider</c>，
    /// 那边每个写工具都包了），而非审批函数会被 <c>ApprovalNotRequiredFunctionBypassingChatClient</c>
    /// 直接旁路。净效果是：<b>连只读档都拦不住模型静默删掉用户的跨会话长期记忆</b>。
    ///
    /// <b>只包 delete</b>：write 与 replace 虽然也是覆盖写，但那是它每轮的正常工作
    /// （框架的系统提示段就在教它随时记录），包了等于每轮弹审批卡，整档能力废掉。
    /// 删除是唯一既不可逆、又没有高频正常场景的那个。
    /// </summary>
    /// <param name="tools">累积到本 provider 的工具集</param>
    /// <returns>删除工具已包审批的工具集</returns>
    internal static List<AITool> GateTools(IEnumerable<AITool> tools)
    {
        List<AITool> result = new();
        foreach (AITool tool in tools)
        {
            result.Add(tool is AIFunction function
                       && function.Name == FileMemoryProvider.DeleteFileToolName
                       && function.GetService<ApprovalRequiredAIFunction>() == null
                ? new ApprovalRequiredAIFunction(function)
                : tool);
        }

        return result;
    }
}
