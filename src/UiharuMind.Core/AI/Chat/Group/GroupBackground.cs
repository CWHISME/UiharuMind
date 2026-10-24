using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 从单聊开群时带进群的背景（方案 v6 §6.6b 规则 5 的轻量落地：另开一个群，原单聊不动）。
/// 让原单聊的模型把那段对话写成一份给新成员看的摘要，填进新群的输入框，由用户过目、删改后再发——
/// 新成员看到什么由用户定。普通群的成员没有工具读不了文件，所以背景是正文而不是文件路径。
/// 复用记忆压缩那条线（同一个客户端与装配好的选项，前缀缓存照吃），只换了写给谁看。
/// 建群之后加人 / 加回时的入群摘要也走这里，由一位群成员来写（ADR 0046 修订「建群之后增删成员」）
/// </summary>
public static class GroupBackground
{
    private const int SummaryCharLimit = 1500; //背景要被每位新成员读一遍，也要让用户看得完

    // 附在记忆压缩指令之后，优先级高于它：那份是写给压缩后的自己，这份是写给没看过这段对话的新成员
    internal const string Audience = """
        这份文档不会替换对话。它会被展示给从这段对话继续开群、以新成员身份加入的角色们——他们完全没看过前面的内容。
        请为他们写：用户想要什么、到目前为止确立了哪些共识、还有什么悬而未决。
        与跟上话题无关的个人细节不必写。
        硬性上限：最多 1500 字——这条覆盖上面的上限。
        """;

    // 新成员入群：他一句都没看过
    internal const string JoinerAudience = """
        这份文档不会替换对话。它会交给一位现在以新成员身份加入本群聊的角色，他完全没看过前面的内容。
        请为他写：用户想要什么、群到目前为止定了什么、成员之间哪里还有分歧、还有什么悬而未决。
        硬性上限：最多 1500 字——这条覆盖上面的上限。
        """;

    // 退群的人加回来：离开之前的他都记得，缺的是最近这一段
    internal const string ReturnerAudience = """
        这份文档不会替换对话。它会交给一位早先在本群聊过、离开了一段时间、现在回来的角色。他记得早期的讨论，但错过了最近这一段。
        请聚焦近况：什么变了、定了什么、还有什么悬而未决。
        硬性上限：最多 1500 字——这条覆盖上面的上限。
        """;

    /// <summary>
    /// 让原单聊的模型写一份背景摘要
    /// </summary>
    /// <param name="source">原单聊</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>摘要正文；没有模型、没有可写的历史或写失败为 null</returns>
    public static Task<string?> WriteAsync(ChatSession source, CancellationToken cancellationToken = default) =>
        WriteForAsync(source, Audience, cancellationToken);

    /// <summary>
    /// 让一位群成员用他自己的模型写入群摘要
    /// </summary>
    /// <param name="writer">写摘要的成员</param>
    /// <param name="returning">读者是加回来的退群成员（否则是新成员）</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>摘要正文；没有模型、没有可写的历史或写失败为 null</returns>
    public static Task<string?> WriteBriefingAsync(ChatSession writer, bool returning,
        CancellationToken cancellationToken = default) =>
        WriteForAsync(writer, returning ? ReturnerAudience : JoinerAudience, cancellationToken);

    /// <summary>
    /// 入群摘要随投递交出时的样子：说明谁写的、写给谁
    /// </summary>
    /// <param name="writerName">写摘要的成员名</param>
    /// <param name="summary">摘要正文</param>
    /// <param name="returning">读者是加回来的退群成员</param>
    /// <returns>投递开头的那一段</returns>
    public static string ComposeBriefing(string writerName, string summary, bool returning) =>
        (returning
            ? $"（你离开群的这段时间，{writerName}整理了一份摘要：）"
            : $"（你刚加入这个群，下面是{writerName}整理的此前讨论摘要：）")
        + "\n\n" + summary.Trim();

    private static async Task<string?> WriteForAsync(ChatSession source, string audience, CancellationToken cancellationToken)
    {
        if (source.ChatModelRunningData is not { ChatClient: not null } model) return null;

        ChatOptions? options = source.Runner.ChatOptions;
        // 与原单聊平时的请求同一视图:同一口径供给、同一套压缩,前缀才中得了它的缓存
        IReadOnlyList<ChatMessage> supplied = await HistorySupply.ForSideRequestAsync(source.History, options,
            model.ContextLength, source.Runner.InputEstimate?.FixedOverhead ?? 0, cancellationToken).ConfigureAwait(false);
        if (supplied.Count == 0) return null;

        HandoffReply reply = await HistoryHandoff.WriteAsync(HistorySupply.ClientFor(source), supplied, options,
            model.ContextLength, audience, cancellationToken).ConfigureAwait(false);
        // 是这个会话的模型花的,记进它的累计:群成员面板的成本读的就是它。不碰占用——单聊本身没有变满
        foreach (UsageDetails usage in reply.Usages)
        {
            // 命中也要记:累计命中率是「累计命中 / 累计输入」,这一发的输入进了分母,
            // 命中不跟着进分子,那个比例就既不是命中率也没法跟官方控制台对账
            source.AccumulateUsage(usage.InputTokenCount ?? 0, usage.OutputTokenCount ?? 0,
                usage.ReasoningTokenCount ?? 0, TurnUsageLedger.ReadCachedTokens(usage));
        }

        if (reply.Usages.Count > 0)
        {
            source.SaveMeta(touchUpdatedAt: false);
            SessionManager.Instance.NotifyUsageReported(source.SessionId);
        }

        return reply.Text == null ? null : HistoryHandoff.Cap(reply.Text, SummaryCharLimit);
    }

    /// <summary>
    /// 新群输入框里的那段背景：说明来由 + 摘要，末尾留空行给用户接着写想问的
    /// </summary>
    /// <param name="characterName">原单聊的角色名</param>
    /// <param name="summary">摘要正文</param>
    /// <returns>草稿正文</returns>
    public static string Compose(string characterName, string summary) =>
        $"（背景：这个群是从我和{characterName}的单聊开出来的，下面是那段对话的摘要。）\n\n{summary.Trim()}\n\n";
}
