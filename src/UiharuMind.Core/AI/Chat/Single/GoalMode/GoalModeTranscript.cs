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

    /// <summary>开启时落一条：目标 + 隐藏扩展用法（模型跨轮靠历史里的这一条不忘目标）</summary>
    /// <param name="goal">目标文本</param>
    /// <returns>注入正文</returns>
    public static string KickoffNote(string goal) =>
        $"【{Marker}】已开启，目标：{goal.Trim()}。" +
        $"若已达成，可调用隐藏扩展结束目标模式：ExCall name=\"goal.end\"，" +
        $"reason=\"done\"（目标达成）或 \"needs_user\"（有必须由用户本人决定的事），" +
        "summary=用户回来时应当知道的交代。";

    /// <summary>每次唤醒轮前的提醒：带<b>当前目标</b>（改目标不落新消息，靠这一句让模型以最新为准；唤醒频率低，多几句消耗可接受）</summary>
    /// <param name="goal">当前目标文本</param>
    /// <returns>注入正文</returns>
    public static string WakeNote(string goal) =>
        $"【{Marker}】当前目标：{goal.Trim()}。继续推进；若已达成，可调用隐藏扩展 goal.end 结束。";
}
