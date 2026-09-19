using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 历史驻留策略。
///
/// 这里覆盖的重点是「不该卸的绝不卸」：卸错一个正在被写的会话，
/// 症状是往历史里写的东西静默消失——没有任何报错，事后也查不出来。
/// </summary>
public class SessionResidencyPolicyTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <param name="idleMinutes">各会话已经冷了多少分钟，下标即会话名后缀</param>
    private static Dictionary<string, DateTime> Resident(params int[] idleMinutes)
    {
        Dictionary<string, DateTime> map = new();
        for (int i = 0; i < idleMinutes.Length; i++)
        {
            map[$"s{i}"] = Now.AddMinutes(-idleMinutes[i]);
        }

        return map;
    }

    [Fact]
    public void UnderTheCap_UnloadsNothing()
    {
        Assert.Empty(SessionResidencyPolicy.SelectForUnload(Resident(10, 20), _ => true, Now, 6));
    }

    /// <summary>超出上限时只让位「超出的那几个」，而且是最冷的那几个</summary>
    [Fact]
    public void OverTheCap_UnloadsTheColdestExcess()
    {
        Dictionary<string, DateTime> resident = Resident(1, 30, 2, 20, 3, 4, 5);

        IReadOnlyList<string> doomed = SessionResidencyPolicy.SelectForUnload(resident, _ => true, Now, 6);

        Assert.Equal(["s1"], doomed);
    }

    /// <summary>
    /// 刚访问过的不卸：后台那几条路都是「Load 出来、跨几个 await 用完再存」，
    /// 中途卸掉会让它们手上那份历史成为孤儿
    /// </summary>
    [Fact]
    public void RecentlyTouched_IsNeverUnloaded()
    {
        Dictionary<string, DateTime> resident = Resident(0, 0, 0, 0, 0, 0, 0, 0);

        Assert.Empty(SessionResidencyPolicy.SelectForUnload(resident, _ => true, Now, 6));
    }

    /// <summary>钉住的、在跑的由调用方一票否决，哪怕它是最冷的那个</summary>
    [Fact]
    public void Vetoed_IsSkippedAndTheNextColdestGoesInstead()
    {
        Dictionary<string, DateTime> resident = Resident(30, 20, 10, 9, 8, 7, 6);

        IReadOnlyList<string> doomed =
            SessionResidencyPolicy.SelectForUnload(resident, id => id != "s0", Now, 6);

        Assert.Equal(["s1"], doomed);
    }

    /// <summary>够冷的不足以填满超出量时，有几个卸几个——不够就不够，不去动没冷却完的</summary>
    [Fact]
    public void NotEnoughColdOnes_UnloadsWhatItCan()
    {
        Dictionary<string, DateTime> resident = Resident(30, 0, 0, 0, 0, 0, 0, 0, 0);

        IReadOnlyList<string> doomed = SessionResidencyPolicy.SelectForUnload(resident, _ => true, Now, 6);

        Assert.Equal(["s0"], doomed);
    }
}
