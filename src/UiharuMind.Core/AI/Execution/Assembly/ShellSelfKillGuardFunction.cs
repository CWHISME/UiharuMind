/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// shell 自杀拦截：杀进程的动词落在应用自己头上时直接拒绝，不进执行。
///
/// 背景：`Shell` 与 `RunBackgroundTask` 的子 shell 和应用是同一用户，
/// `pkill -f UiharuMind.Desktop`、`kill &lt;本进程pid&gt;` 能亲手杀掉自己——
/// 日志断在半截、飞着的步骤也回不来了（2026-10-12 实测：为换档案重起连杀四次，
/// 最后一刀 `kill 85492` 正中自己）。提示词拦不住（绕过去了）、审批也拦不住
/// （点了就是点了，“记住同类”还会把 `pkill -f*` 一并记住），只能在执行层硬拦。
///
/// 位置刻意在 <see cref="ApprovalRequiredAIFunction"/> 里面：框架靠最外层认出“需审批”，
/// 包到外面会把审批信号藏掉。拦的是“执行”不是“审批”——批了也照样拒绝，不打扰用户点那一下。
/// 判据是保守的文本匹配（杀进程动词 + 同段内的目标），混淆写法（`k'il'l`、变量间接、
/// `$(pgrep …)` 裸 pid）拦不住：它是护栏不是沙箱，真对抗得靠沙箱。
/// </summary>
internal sealed class ShellSelfKillGuardFunction : DelegatingAIFunction
{
    /// <summary>拒绝文案开头（测试按它认拒绝）</summary>
    internal const string RefusalPrefix = "拒绝执行：";

    private const int SnippetMaxLength = 80;

    // 杀进程动词逐个列：`skill` 这类词里也含 kill，裸写 kill 反而误伤，所以全用 \b 圈住；
    // taskkill/stop-process 不含 \bkill\b（前者是连着的、后者是另一个词），另列。
    private static readonly Regex KillVerb =
        new(@"\b(pkill|kill|killall|taskkill|stop-process)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>应用自己的名字：进程名、包名、产物名都含这一截</summary>
    private static readonly Regex AppToken =
        new(@"uiharumind", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>stateless shell 的父进程就是应用自己，`kill $PPID` 即自杀（`${PPID}` 同）</summary>
    private static readonly Regex ParentPidToken =
        new(@"\$\{?PPID\}?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly int OwnPid = Environment.ProcessId;
    private static readonly Regex OwnPidToken = new(@"\b" + OwnPid + @"\b", RegexOptions.Compiled);

    public ShellSelfKillGuardFunction(AIFunction innerFunction)
        : base(innerFunction)
    {
    }

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // 参数口径复用审批那一份：shell 两把工具的命令参数都叫 command
        string? hit = FindSelfKill(ApprovalModeMapper.ExtractCommand(arguments), OwnPid);
        if (hit != null)
            return ValueTask.FromResult<object?>(BuildRefusal(hit));
        return base.InvokeCoreAsync(arguments, cancellationToken);
    }

    /// <summary>
    /// 找出命令里杀应用自己的那一段：杀进程动词之后、同一段里出现应用名、本进程 pid 或 `$PPID`。
    /// 段到 `;`、`&`、换行即止，管道不断（`ps | xargs kill` 仍算一段）。
    /// 不剥 heredoc 正文：`bash &lt;&lt;EOF` 的正文会被执行，剥了就是一条绕过的路；
    /// 写脚本文件时正文里提到 pkill 会误伤，由拒绝文案点出命中段、指向 `Write`。
    /// </summary>
    /// <param name="command">命令原文</param>
    /// <param name="ownPid">本进程 pid（参数化只为可测试，线上恒为 <see cref="Environment.ProcessId"/>）</param>
    /// <returns>命中的那一段（动词起）；不是自杀时为 null</returns>
    internal static string? FindSelfKill(string? command, int ownPid)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        Regex pidToken = ownPid == OwnPid ? OwnPidToken : new Regex(@"\b" + ownPid + @"\b", RegexOptions.Compiled);
        foreach (Match verb in KillVerb.Matches(command))
        {
            string tail = command[(verb.Index + verb.Length)..].Split([';', '&', '\r', '\n'])[0];
            if (AppToken.IsMatch(tail) || ParentPidToken.IsMatch(tail) || pidToken.IsMatch(tail))
                return (verb.Value + tail).Trim();
        }
        return null;
    }

    internal static string BuildRefusal(string hit)
    {
        string snippet = hit.Length > SnippetMaxLength ? hit[..SnippetMaxLength] + "…" : hit;
        return RefusalPrefix + $"命中「{snippet}」，这段会杀掉 UiharuMind 应用自己的进程（Shell 就跑在这个进程里，杀它等于自杀）。" +
               "要停应用走 `app quit` 这类公开步骤并先问用户，没有就让用户手动操作，别自己动手杀进程。" +
               "若这段只是要写进文件的脚本内容（如 heredoc 正文），改用 `Write` 写文件。";
    }
}
