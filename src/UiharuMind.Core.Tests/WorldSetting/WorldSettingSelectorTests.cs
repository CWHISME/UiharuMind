using UiharuMind.Core.AI.WorldSettings;

namespace UiharuMind.Core.Tests.WorldSettings;

public class WorldSettingSelectorTests
{
    private static WorldSetting Setting(params WorldSettingEntry[] entries) => new() { Entries = [.. entries] };

    [Fact]
    public void Select_NullOrEmpty_ReturnsEmpty()
    {
        Assert.True(WorldSettingSelector.Select(null, "任意文本").IsEmpty);
        Assert.True(WorldSettingSelector.Select(new WorldSetting(), "任意文本").IsEmpty);
    }

    [Fact]
    public void Select_ConstantEntry_InjectedWithoutKeywords()
    {
        var setting = Setting(new WorldSettingEntry { Constant = true, Content = "常驻条目" });

        WorldSettingSelection selection = WorldSettingSelector.Select(setting, "");

        Assert.False(selection.IsEmpty);
        Assert.Contains(selection.BeforeCharacter, e => e.Content == "常驻条目");
    }

    [Fact]
    public void Select_KeywordHit_InjectsMatchingEntry()
    {
        var setting = Setting(new WorldSettingEntry { Keys = ["学园都市", "第七学区", "Academy City"], Content = "地点" });

        Assert.False(WorldSettingSelector.Select(setting, "我们在学园都市碰头。").IsEmpty);
        // 多关键词任一命中即可；大小写不敏感
        Assert.False(WorldSettingSelector.Select(setting, "Let's meet at Academy City.").IsEmpty);
    }

    [Fact]
    public void Select_KeywordMiss_DoesNotInject()
    {
        var setting = Setting(new WorldSettingEntry { Keys = ["学园都市"], Content = "地点" });

        Assert.True(WorldSettingSelector.Select(setting, "聊聊天气吧。").IsEmpty);
    }

    [Fact]
    public void Select_EmptyScanText_OnlyConstantsInject()
    {
        var setting = Setting(
            new WorldSettingEntry { Keys = ["学园都市"], Content = "关键词条目" },
            new WorldSettingEntry { Constant = true, Content = "常驻条目" });

        WorldSettingSelection selection = WorldSettingSelector.Select(setting, "");

        Assert.False(selection.IsEmpty);
        Assert.Empty(selection.BeforeCharacter.Where(e => e.Keys.Count > 0));
    }

    [Fact]
    public void Select_GroupsByPosition()
    {
        var setting = Setting(
            new WorldSettingEntry { Keys = ["x"], Content = "后段", Position = EWorldSettingPosition.AfterCharacter },
            new WorldSettingEntry { Keys = ["x"], Content = "前段", Position = EWorldSettingPosition.BeforeCharacter });

        WorldSettingSelection selection = WorldSettingSelector.Select(setting, "x");

        Assert.Equal("前段", Assert.Single(selection.BeforeCharacter).Content);
        Assert.Equal("后段", Assert.Single(selection.AfterCharacter).Content);
    }

    [Fact]
    public void Select_OrdersWithinPosition()
    {
        var setting = Setting(
            new WorldSettingEntry { Keys = ["x"], Content = "后写入", Order = 2 },
            new WorldSettingEntry { Keys = ["x"], Content = "先写入", Order = 1 });

        WorldSettingSelection selection = WorldSettingSelector.Select(setting, "x");

        Assert.Equal(["先写入", "后写入"], selection.BeforeCharacter.Select(e => e.Content).ToArray());
    }

    /// <summary>
    /// 全量第一条无论多大都收：预算比单条还小的时候，交一条总比交空手好。
    /// 只豁免第一条，前后两组不会各自豁免一条把预算打穿到两条最大条目之和。
    /// </summary>
    [Fact]
    public void Select_RespectsTokenBudget_OnlyFirstOverallExempt()
    {
        var setting = new WorldSetting
        {
            TokenBudget = 100,
            Entries =
            [
                new WorldSettingEntry { Keys = ["alpha"], Content = "AAAA" + new string('A', 1000), Order = 0 },
                new WorldSettingEntry
                {
                    Keys = ["beta"], Position = EWorldSettingPosition.AfterCharacter,
                    Content = "BBBB" + new string('B', 1000), Order = 0,
                },
            ],
        };

        WorldSettingSelection selection = WorldSettingSelector.Select(setting, "alpha beta");

        // 旧语义「两组各豁免首条」会给 2 条；现在全量只豁免第一条，预算耗尽即停
        Assert.Equal(1, selection.BeforeCharacter.Count + selection.AfterCharacter.Count);
        Assert.Equal("AAAA", selection.BeforeCharacter[0].Content[..4]);
    }

    [Fact]
    public void Select_RespectsTokenBudget_FirstInGroupAlwaysInjected()
    {
        var setting = new WorldSetting
        {
            TokenBudget = 100,
            Entries =
            [
                new WorldSettingEntry { Keys = ["alpha"], Content = "AAAA" + new string('A', 1000), Order = 0 },
                new WorldSettingEntry { Keys = ["beta"], Content = "应被预算截断", Order = 1 },
            ],
        };

        WorldSettingSelection selection = WorldSettingSelector.Select(setting, "alpha beta");

        Assert.Single(selection.BeforeCharacter);
        Assert.StartsWith("AAAA", selection.BeforeCharacter[0].Content);
    }

    [Fact]
    public void Format_ProducesGroupedBlock()
    {
        var selection = new WorldSettingSelection(
            [new WorldSettingEntry { Content = "前段内容" }],
            [new WorldSettingEntry { Content = "后段内容" }]);

        string formatted = WorldSettingSelector.Format(selection);

        Assert.Contains("Before the character definition:", formatted);
        Assert.Contains("After the character definition:", formatted);
        Assert.Contains("前段内容", formatted);
        Assert.Contains("后段内容", formatted);
    }

    [Fact]
    public void Format_EmptySelection_ReturnsEmpty()
    {
        Assert.Equal("", WorldSettingSelector.Format(new WorldSettingSelection([], [])));
    }
}