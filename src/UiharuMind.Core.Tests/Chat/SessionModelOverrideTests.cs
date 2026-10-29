using System.Text.Json;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 会话覆写（单会话模型设置）：名为空=还没跑过、跟随全局，非空=钉选（含首轮定格）。
/// 钉死持久化契约——索引、头文件与 ToMeta 三处必须一致，老存档缺字段读回即默认。
/// </summary>
public class SessionModelOverrideTests
{
    [Fact]
    public void PinIfFollowingGlobal_StampsServingModel()
    {
        // 没选过模型的会话在首轮开跑时定格成本轮实际用的那个：之后换全局模型不再牵动它，
        // 否则下一发换窗口，整段前缀缓存作废
        ChatSession session = new() { IsTransient = true }; //临时会话不落盘,测试不碰真实档案

        Assert.True(session.PinModelIfFollowingGlobal(new ModelRunningData(new GGufModelInfo { ModelName = "m1" })));

        Assert.Equal("m1", session.SessionModelName);
        Assert.True(session.HasSessionModelOverride);
        Assert.Equal("m1", session.ToMeta().SessionModelName);
    }

    [Fact]
    public void PinIfFollowingGlobal_LeavesPinnedModelAlone()
    {
        // 钉过的不动（用户手选的、子代理指定的都在这条上）
        ChatSession session = new() { IsTransient = true, SessionModelName = "m1" };

        Assert.False(session.PinModelIfFollowingGlobal(new ModelRunningData(new GGufModelInfo { ModelName = "m2" })));

        Assert.Equal("m1", session.SessionModelName);
    }

    [Fact]
    public void PinIfFollowingGlobal_DoesNothingWithoutModel()
    {
        // 一个模型都定不出时不落空名字——空串会被当成「已钉选」，那之后再也跟随不了全局
        ChatSession session = new() { IsTransient = true };

        Assert.False(session.PinModelIfFollowingGlobal(null));

        Assert.Null(session.SessionModelName);
        Assert.False(session.HasSessionModelOverride);
    }

    [Fact]
    public void NewSession_HasNoOverride()
    {
        ChatSession session = new();

        Assert.Null(session.SessionModelName);
        Assert.False(session.HasSessionModelOverride);
        Assert.Null(session.ToMeta().SessionModelName);
    }

    [Fact]
    public void SettingRunningData_StampsPersistedName()
    {
        // 临时会话转正走 JSON 落盘，JsonIgnore 的运行期覆写带不过去——
        // setter 把名字同步盖到持久化字段，转正后覆写不丢
        ChatSession session = new();
        session.ChatModelRunningData = new ModelRunningData(new GGufModelInfo { ModelName = "m1" });

        Assert.Equal("m1", session.SessionModelName);
        Assert.True(session.HasSessionModelOverride);
    }

    [Fact]
    public void ToMeta_CarriesOverrideName()
    {
        ChatSession session = new() { SessionModelName = "m1" };

        Assert.Equal("m1", session.ToMeta().SessionModelName);
    }

    [Fact]
    public void RoundTrip_KeepsOverrideName()
    {
        ChatSession session = new() { SessionModelName = "m1" };

        string json = JsonSerializer.Serialize(session, SessionJsonOptions.Default);
        ChatSession restored = JsonSerializer.Deserialize<ChatSession>(json, SessionJsonOptions.Default)!;

        Assert.Equal("m1", restored.SessionModelName);
        Assert.True(restored.HasSessionModelOverride);
        Assert.Equal("m1", restored.ToMeta().SessionModelName);
    }

    [Fact]
    public void OldArchive_WithoutField_ReadsAsDefault()
    {
        ChatSession restored = JsonSerializer.Deserialize<ChatSession>(
            """{"SessionId":"s1","Title":"t","FormatVersion":4}""", SessionJsonOptions.Default)!;

        Assert.Null(restored.SessionModelName);
        Assert.False(restored.HasSessionModelOverride);
    }
}
