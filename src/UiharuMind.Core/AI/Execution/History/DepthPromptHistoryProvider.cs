using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 深度注入包装器：把角色卡 depth_prompt 的文本插到<b>供给模型</b>的历史倒数第 Depth 条的位置。
///
/// 只改「模型看到的那一份」，不改会话历史本体——供给与落盘都委托给被包装的 provider，
/// 本类通过覆写 <see cref="InvokingCoreAsync"/> 在两者之间截断改写 RequestMessages，
/// <see cref="InvokedCoreAsync"/> 原样委托给内层（落盘、凭据、界面通知都在那边）。
/// 装配时按角色有无 depth_prompt 决定要不要套这层：没有就是纯透传，不套省掉每轮一次委托。
/// </summary>
internal sealed class DepthPromptHistoryProvider : ChatHistoryProvider
{
    private readonly ChatHistoryProvider _inner;
    private readonly string _prompt;
    private readonly int _depth;
    private readonly ChatRole _role;

    public DepthPromptHistoryProvider(ChatHistoryProvider inner, DepthPromptInfo prompt)
    {
        _inner = inner;
        _prompt = prompt.Prompt;
        _depth = Math.Max(0, prompt.Depth);
        _role = prompt.ToChatRole();
    }

    public override IReadOnlyList<string> StateKeys => _inner.StateKeys;

    protected override async ValueTask<IEnumerable<ChatMessage>> InvokingCoreAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        // 内层的公开入口返回<b>供给好的历史</b>，直接用它而不是读 context.RequestMessages：
        // 该属性由框架在调用前填入本轮调用方的消息，不含供给历史——从它取历史，
        // 真实冒烟里整段历史静默丢失（当前注释所称的实机记录即是）。
        IEnumerable<ChatMessage> supplied =
            await _inner.InvokingAsync(context, cancellationToken).ConfigureAwait(false);

        List<ChatMessage> messages = (supplied ?? []).ToList();
        Splice(messages, _prompt, _role, _depth);
        return messages;
    }

    protected override ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
        => _inner.InvokedAsync(context, cancellationToken);

    /// <summary>
    /// 把提示插到消息列表倒数第 depth 条的位置（纯函数，可单测）：
    /// depth=0 插在最后；depth=1 插在最后一条之前；越界收束到开头。
    /// </summary>
    internal static void Splice(List<ChatMessage> messages, string prompt, ChatRole role, int depth)
    {
        if (string.IsNullOrWhiteSpace(prompt) || messages.Count == 0) return;
        int index = Math.Max(0, messages.Count - Math.Max(0, depth));
        messages.Insert(index, new ChatMessage(role, prompt));
    }
}