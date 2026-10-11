using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Prompts;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 脑工具（ADR 0082）：同步、有界、只读的小模型整理助手。
///
/// 大文件/被截断的工具结果交给它总结：主代理那一轮<b>同步等它跑完</b>，
/// 当场返回整理好的内容 + <c>[sub-session: 短号]</c> 标识（复用委派的标识契约，
/// 工具卡据此挂「查看更多」入口）。想深挖就带 brainId 追问，它记得之前的问答。
///
/// 机制上是开或续一个<b>真子会话</b>（<c>SubAgentType.Brain</c>），装配只给 Grep/Read，
/// 改不了任何东西——只读，没有审批一说。轮次编排与委派共用
/// <see cref="SubAgentTool.RunSubSessionTurnAsync"/>，差异只有墙钟上限
/// （<see cref="Timeout"/> 5 分钟）与无审批（attended=false）。
///
/// 不变量：脑会话<b>不带委派工具</b>（递归禁止），工具集恒定 Grep/Read 无 Glob。
/// 均由测试钉住（见 AssemblyInvariantsTests / BrainToolTests）。
/// </summary>
public static class BrainTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量,写死字面量迟早对不上</summary>
    public const string ToolName = "Brain";

    /// <summary>脑的墙钟上限：有界活，到限返回已得部分/失败，可带 brainId 追问续跑（ADR 0082）</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>脑的内部工具轮上限：有界活，到顶即停止循环并把已有进展作为响应返回（ADR 0082）</summary>
    public const int MaxIterations = 128;

    /// <summary>返回的整理结果预算（UTF-8 字节）。超了自截断并注明可追问——拿截断去治截断没有意义（ADR 0082）</summary>
    public const int ReportBudgetBytes = 16 * 1024;

    /// <summary>截断附言：主代理据此知道还有细节可挖</summary>
    public const string TruncationNote = "\n【整理结果较长已截断；更细可通过 brainId 追问。】";

    /// <summary>
    /// 创建脑工具。上下文与委派同一形状（<see cref="SubAgentTool.LaunchContext"/>），
    /// 装配时以 <see cref="SubAgentProfile.Brain"/> 传入。
    /// </summary>
    /// <param name="context">派活上下文</param>
    /// <returns>工具实例</returns>
    public static AITool Create(SubAgentTool.LaunchContext context) =>
        AIFunctionFactory.Create(
            async ([Description(BrainToolPrompts.TaskParam)] string task,
                   [Description(BrainToolPrompts.BrainIdParam)] string? brainId = null,
                   CancellationToken cancellationToken = default) =>
                await RunAsync(context, task, brainId, cancellationToken).ConfigureAwait(false),
            ToolName,
            BrainToolPrompts.Description);

    /// <summary>
    /// 开新脑或追问：当场同步跑完，返回内容 + 标识。
    /// </summary>
    internal static async Task<string> RunAsync(SubAgentTool.LaunchContext context, string task,
        string? brainId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task)) return "Error: task 不能为空。";

        ChatSession? session = string.IsNullOrWhiteSpace(brainId)
            ? CreateBrain(context, task)
            : LoadBrainForFollowUp(context, brainId);
        if (session == null) return $"Error: 没有 '{brainId}' 这个脑。";

        // 先交界面再跑：工具卡要在跑着的时候就能「查看更多」（同委派的 NoteStarted）
        SubAgentTool.NoteStarted(context, session.SessionId);
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(Timeout);
        string report = await SubAgentTool.RunSubSessionTurnAsync(session, task, attended: false,
                Timeout, timeoutSource, cancellationToken)
            .ConfigureAwait(false);
        return BuildReceipt(session.SessionId, report);
    }

    /// <summary>开一个新脑：真子会话，钉 Brain 档与脑模型（空 = 跟随主代理/全局）</summary>
    private static ChatSession CreateBrain(SubAgentTool.LaunchContext context, string task)
    {
        SubAgentTool.ModelChoice model = SubAgentTool.ResolveSubAgentModelName(context.Profile);
        ChatSession session = SubAgentTool.CreateSubSession(context, ESubAgentType.Brain,
            DefaultCharacter.AnonymousAgent.ToString(), SubAgentTool.BuildTitle(task), task,
            string.Empty, string.Empty, model.Name);
        SessionManager.Instance.Add(session);
        return session;
    }

    /// <summary>
    /// 带 brainId 追问：只认本会话派出去的<b>真脑会话</b>。
    /// 短号/完整 ID 都收（<see cref="SubSessionIdAlias.Match"/>）；撞号、查无、不是本会话的、不是脑的，
    /// 一律按「没有这个脑」收口。
    /// </summary>
    internal static ChatSession? LoadBrainForFollowUp(SubAgentTool.LaunchContext context, string? brainId)
    {
        string? target = SubAgentTool.NormalizeTo(brainId);
        if (target == null) return null;
        IReadOnlyList<ChatSessionMeta> runs = SubSessionIdAlias.Match(context.ParentSessionId, target);
        if (runs.Count != 1) return null;
        ChatSession? session = SessionManager.Instance.Load(runs[0].SessionId);
        if (session == null) return null;
        if (!string.Equals(session.ParentSessionId, context.ParentSessionId, StringComparison.Ordinal)) return null;
        if (!SubAgentProfile.IsBrainLineage(session.SubAgentType)) return null;
        return session;
    }

    /// <summary>
    /// 回执：内容在前，末行 <c>[sub-session: 短号]</c>。末行是<b>跨模块契约</b>——
    /// <c>ToolCallItem.ParseSubSessionId</c> 认末行给工具卡挂「查看更多」入口。
    /// </summary>
    public static string BuildReceipt(string subSessionId, string report) =>
        CapReport(report) + "\n" + $"[sub-session: {SubSessionIdAlias.Short(subSessionId)}]";

    /// <summary>
    /// 整理结果预算裁剪：超过 <see cref="ReportBudgetBytes"/> 就按 UTF-8 字节截断到字符边界
    /// （不劈开多字节字符），并附一句「更细可追问 brainId」。纯函数，可单测。
    /// </summary>
    public static string CapReport(string report, int budgetBytes = ReportBudgetBytes)
    {
        string trimmed = report.TrimEnd();
        byte[] bytes = Encoding.UTF8.GetBytes(trimmed);
        if (bytes.Length <= budgetBytes) return trimmed;

        // 退到 UTF-8 前导字节边界，不劈开多字节字符
        int end = budgetBytes;
        while (end > 0 && (bytes[end] & 0xC0) == 0x80) end--;
        return Encoding.UTF8.GetString(bytes, 0, end) + TruncationNote;
    }
}
