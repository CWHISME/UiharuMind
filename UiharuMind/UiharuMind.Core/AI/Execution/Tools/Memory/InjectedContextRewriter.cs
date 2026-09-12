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
/// <item><description><b>记忆索引消息整条过滤</b>——见 <see cref="Rewrite"/>。</description></item>
/// <item><description><b>删除工具没有审批</b>——见 <see cref="GateTools"/>。</description></item>
/// </list>
/// Todo 与 mode 两个 provider 是同一套注入套路，暂不纳入：待办清单每轮变化且模型确实需要
/// 每轮看见，与记忆索引不是同一种语义。故规则做成按来源分发，加一档只是加一个分支。
/// </summary>
internal sealed class InjectedContextRewriter : AIContextProvider
{
    /// <summary>框架 provider 的类型全名，即它盖在注入消息上的来源标识</summary>
    private static readonly string FileMemorySourceId = typeof(FileMemoryProvider).FullName!;

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
            ChatMessage? rewritten = Rewrite(message);
            if (rewritten != null) result.Add(rewritten);
        }

        return result;
    }

    private static ChatMessage? Rewrite(ChatMessage message)
    {
        if (message.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.AIContextProvider)
            return message;

        // 记忆索引消息整条过滤掉:记忆的存在与用法已在系统提示里常驻(框架注入的
        // ## File Based Memory 段),这份索引只是快捷清单,model 用到时用 file_memory_ls/grep/read
        // 主动取新鲜数据即可;因此此处直接以 null 丢弃,不再注入任何清单或指针
        return message.GetAgentRequestMessageSourceId() == FileMemorySourceId ? null : message;
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