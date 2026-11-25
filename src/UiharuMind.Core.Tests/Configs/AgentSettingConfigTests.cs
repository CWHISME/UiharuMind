using System.Text.Json;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests.Configs;

/// <summary>
/// 子代理模型那几列（ADR 0111）读档时的基本形状。
///
/// 没有旧档迁移：这一版明确不做兼容——旧的单值键升级后直接作废，用户在设置页重配一遍即可。
/// 因此这里只管「新形状读得对」，尤其是 <c>null</c> 不能递到派活侧（那里会拿它直接轮换）。
/// </summary>
public class AgentSettingConfigTests
{
    /// <summary>手改的档里写着 <c>null</c> 也当没配：派活侧拿到的必须是可枚举的名单</summary>
    [Fact]
    public void NullListInFile_ReadsAsEmpty()
    {
        AgentSettingConfig config = SaveUtility.LoadFromString<AgentSettingConfig>("""
            { "GeneralSubAgentModelNames": null }
            """);

        Assert.NotNull(config.GeneralSubAgentModelNames);
        Assert.Empty(config.GeneralSubAgentModelNames);
    }

    /// <summary>三档各自独立：读一档不该把别档带出来</summary>
    [Fact]
    public void Lists_AreIndependentPerTier()
    {
        AgentSettingConfig config = SaveUtility.LoadFromString<AgentSettingConfig>("""
            {
              "GeneralSubAgentModelNames": ["a"],
              "BrainSubAgentModelNames": [],
              "ExplorerSubAgentModelNames": ["b", "c"]
            }
            """);

        Assert.Equal(new[] { "a" }, config.GeneralSubAgentModelNames);
        Assert.Empty(config.BrainSubAgentModelNames);
        Assert.Equal(new[] { "b", "c" }, config.ExplorerSubAgentModelNames);
    }

    /// <summary>顺序就是轮换顺序，落盘再读回来不能被打散</summary>
    [Fact]
    public void Order_SurvivesRoundTrip()
    {
        AgentSettingConfig config = new() { GeneralSubAgentModelNames = ["z", "a", "m"] };

        using JsonDocument saved = JsonDocument.Parse(SaveUtility.SaveToString(config));
        JsonElement names = saved.RootElement.GetProperty("GeneralSubAgentModelNames");

        Assert.Equal(new[] { "z", "a", "m" },
            names.EnumerateArray().Select(x => x.GetString()));
    }
}
