using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Chat.Single.GoalMode;

/// <summary>
/// 目标模式的注入文本：与离席简报同一种【】包裹形态，载体是 User 角色消息 + 旁白标注
/// （见 <see cref="GoalModeController"/>），UI 据此画成旁白而不是普通气泡。
/// 模型读起来像系统替用户转达的交代；唤醒轮仍以无消息形态跑，因此不算「用户参与」
/// </summary>
public static class GoalModeTranscript
{
    /// <summary>注入块的标签</summary>
    public const string Marker = "目标模式";

    /// <summary>开启时落一条：目标 + 隐藏扩展用法（模型跨轮靠历史里的这一条不忘目标），
    /// 并说明结束后不会再被唤醒——免得模型拿 goal.end 当「歇一会儿」用</summary>
    /// <param name="goal">目标文本</param>
    /// <param name="now">开启的时刻（模型手上没有别的时钟，首轮也把现在几点告诉它）</param>
    /// <returns>注入正文</returns>
    public static string KickoffNote(string goal, DateTimeOffset now) =>
        $"【{Marker}】已开启，现在是 {now:yyyy-MM-dd HH:mm}。目标：{goal.Trim()}。" +
        $"若已达成，可调用隐藏扩展结束目标模式：ExCall name=\"goal.end\"，" +
        $"reason=\"done\"（目标达成）或 \"needs_user\"（有必须由用户本人决定的事），" +
        "summary=用户回来时应当知道的交代。" +
        "结束后你不会再被唤醒，等于进入永久休眠，除非用户重新开启目标模式——" +
        "所以别拿它当「先歇一会儿」，只有确实达成或非用户本人决定不可时才结束。";

    /// <summary>
    /// 每次唤醒轮前的提醒：带<b>当前目标</b>（改目标不落新消息，靠这一句让模型以最新为准；唤醒频率低，多几句消耗可接受），
    /// 以及这一轮的时间——模型手上没有别的时钟，现在几点、上一次从何时起休眠了多久只能从这里知道
    /// </summary>
    /// <param name="goal">当前目标文本</param>
    /// <param name="now">这一轮的时刻</param>
    /// <param name="lastSleepStart">上一次开始休眠的时刻（即会话上次停下的时刻，取会话本体的记录，不另存）</param>
    /// <returns>注入正文</returns>
    public static string WakeNote(string goal, DateTimeOffset now, DateTimeOffset lastSleepStart) =>
        $"【{Marker}】{TimeNote(now, lastSleepStart)}当前目标：{goal.Trim()}。" +
        "继续推进；若已达成，可调用隐藏扩展 goal.end 进入永久休眠。";

    // 时间那一句：现在几点、上一次从何时起休眠、已过去多久（时钟回拨按 0 算，不给模型负时长）
    private static string TimeNote(DateTimeOffset now, DateTimeOffset lastSleepStart)
    {
        TimeSpan slept = now > lastSleepStart ? now - lastSleepStart : TimeSpan.Zero;
        return $"现在是 {now:yyyy-MM-dd HH:mm}。上次开始休眠是 {lastSleepStart:yyyy-MM-dd HH:mm}，" +
               $"已过去 {TimeUtils.FormatDuration(slept)}。";
    }
}
