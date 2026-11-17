using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 带留痕的闸门等待。
///
/// <para>
/// 这些闸门（执行者闸门、子会话轮次闸门、交回闸门）都是<b>无上限等待</b>的：持有者是一轮对话，
/// 而一轮对话可能是几十分钟，也可能永远不结束（半开连接、停不下来的工具）。
/// 等待本身没有错，错的是它<b>一条日志都不留</b>——真卡住时现场只剩「界面显示运行中」这一条线索，
/// 排查只能靠翻代码猜。
/// </para>
/// <para>
/// 所以这里只加一件事：等超过 <see cref="NoticeAfter"/> 就记一行，然后<b>继续等</b>。
/// 行为一个字没变，但下次卡住时日志里会有「谁在等谁」。
/// </para>
/// </summary>
internal static class GateWait
{
    /// <summary>等多久开始记一行（远大于正常排队：正常排队是毫秒级）</summary>
    internal static readonly TimeSpan NoticeAfter = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 进闸门；等太久就记一行再继续等
    /// </summary>
    /// <param name="gate">闸门</param>
    /// <param name="label">日志里认得出是哪儿在等</param>
    /// <param name="cancellationToken">取消标记；能传就传，这是唯一的脱身通道</param>
    /// <param name="noticeAfter">多久算「等太久」，默认 <see cref="NoticeAfter"/></param>
    public static async Task EnterAsync(SemaphoreSlim gate, string label,
        CancellationToken cancellationToken = default, TimeSpan? noticeAfter = null)
    {
        Task wait = gate.WaitAsync(cancellationToken);
        if (wait.IsCompleted)
        {
            await wait.ConfigureAwait(false);
            return;
        }

        TimeSpan limit = noticeAfter ?? NoticeAfter;
        using CancellationTokenSource notice = new();
        Task delay = Task.Delay(limit, notice.Token);
        if (await Task.WhenAny(wait, delay).ConfigureAwait(false) != wait)
        {
            Log.Warning($"{label} has been queued on a gate for over {limit.TotalSeconds:0}s: "
                        + "the holder is likely a turn that never ended.");
        }

        notice.Cancel();
        await wait.ConfigureAwait(false);
    }
}
