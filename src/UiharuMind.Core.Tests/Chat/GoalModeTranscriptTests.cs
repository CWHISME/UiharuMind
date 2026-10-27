using UiharuMind.Core.AI.Chat.Single.GoalMode;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 目标模式的两处注入文本：开启时与每次唤醒。开启那条只交代现在几点，
/// 唤醒那条另交代「上一次从何时起休眠、已过去多久」——模型没有别的时钟
/// </summary>
public class GoalModeTranscriptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 17, 50, 0, TimeSpan.FromHours(8));

    [Fact]
    public void KickoffNote_CarriesNow_SoTheFirstTurnAlsoKnowsTheClock()
    {
        string note = GoalModeTranscript.KickoffNote("写一份方案", Now);

        Assert.Contains("现在是 2026-10-27 17:50", note);
        Assert.Contains("目标：写一份方案", note);
        Assert.Contains("goal.end", note);
    }

    [Fact]
    public void WakeNote_CarriesNow_TheSleepStart_AndTheSleep()
    {
        string note = GoalModeTranscript.WakeNote("写一份方案", Now, Now.AddMinutes(-90));

        Assert.Contains("现在是 2026-10-27 17:50", note);
        Assert.Contains("上次开始休眠是 2026-10-27 16:20", note);
        Assert.Contains("已过去 1 小时 30 分", note);
        Assert.Contains("当前目标：写一份方案", note);
        Assert.Contains("goal.end", note);
    }

    [Theory]
    [InlineData(30, "30 秒")]
    [InlineData(300, "5 分")]
    [InlineData(3900, "1 小时 5 分")]
    public void Sleep_ReadsLikeWords(int seconds, string expected)
    {
        Assert.Contains($"已过去 {expected}", GoalModeTranscript.WakeNote("目标", Now, Now.AddSeconds(-seconds)));
    }

    /// <summary>时钟回拨 / 假时钟后退：休眠起点晚于现在时按 0 算，不给模型负时长</summary>
    [Fact]
    public void BackwardsClock_ClampsTheSleepToZero()
    {
        Assert.Contains("已过去 0 秒", GoalModeTranscript.WakeNote("目标", Now, Now.AddMinutes(10)));
    }
}
