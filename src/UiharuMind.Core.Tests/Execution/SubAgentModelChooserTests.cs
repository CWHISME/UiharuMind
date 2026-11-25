using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 派活时挑模型：<b>点名 &gt; 档位默认（多选时依次取）&gt; 派活者/全局</b>（ADR 0031 + ADR 0111）。
///
/// 这里钉的是「一次派活一张票」这条语义：点名且钉得住的那一趟<b>不该</b>推进轮换游标，
/// 否则用户偶尔点一次名，后面几趟的档位默认就整体错位了。判据用假探针注入，
/// 不碰真模型库——真去钉一个模型要拉起运行时。
/// </summary>
[Collection("SubAgentModelRotation")]
public class SubAgentModelChooserTests
{
    public SubAgentModelChooserTests() => SubAgentModelRotation.Reset();

    /// <summary>没点名：按配置顺序依次取，回执里不带任何交代</summary>
    [Fact]
    public void NoRequest_TakesConfiguredModelsInTurn()
    {
        AgentSettingConfig config = new() { GeneralSubAgentModelNames = { "a", "b" } };
        FakeProbe probe = new("a", "b");

        ModelChoice first = SubAgentModelChooser.Pick(SubAgentProfile.General, null, config, probe);
        ModelChoice second = SubAgentModelChooser.Pick(SubAgentProfile.General, null, config, probe);

        Assert.Equal("a", first.Name);
        Assert.Equal("b", second.Name);
        Assert.Equal(string.Empty, first.Notice);
    }

    /// <summary>点名钉得住：这一趟用点名的，且不推进游标——下一趟的档位默认还是第一条</summary>
    [Fact]
    public void PinnedRequest_DoesNotAdvanceRotation()
    {
        AgentSettingConfig config = new() { GeneralSubAgentModelNames = { "a", "b" } };
        FakeProbe probe = new("a", "b", "named");

        ModelChoice named = SubAgentModelChooser.Pick(SubAgentProfile.General, "named", config, probe);
        ModelChoice next = SubAgentModelChooser.Pick(SubAgentProfile.General, null, config, probe);

        Assert.Equal("named", named.Name);
        Assert.Equal("a", next.Name);
    }

    /// <summary>点名的模型库里有、但此刻跑不起来：回落档位默认，并且必须把实情写进回执</summary>
    [Fact]
    public void KnownButUnusableRequest_FallsBackToConfigured_AndSaysWhy()
    {
        AgentSettingConfig config = new() { GeneralSubAgentModelNames = { "a" } };
        FakeProbe probe = new(pinnable: "a") { Known = { "named" } };

        ModelChoice pick = SubAgentModelChooser.Pick(SubAgentProfile.General, "named", config, probe);

        Assert.Equal("a", pick.Name);
        Assert.Contains("没有在运行", pick.Notice);
        Assert.Contains("'a'", pick.Notice);
    }

    /// <summary>名字压根不存在时说的是另一句话：两句话混了，排查方向就错了</summary>
    [Fact]
    public void UnknownRequest_SaysTheNameDoesNotExist()
    {
        AgentSettingConfig config = new() { GeneralSubAgentModelNames = { "a" } };

        ModelChoice pick = SubAgentModelChooser.Pick(SubAgentProfile.General, "ghost", config,
            new FakeProbe("a"));

        Assert.Equal("a", pick.Name);
        Assert.Contains("没有名为 'ghost' 的模型", pick.Notice);
    }

    /// <summary>档位默认一条都钉不住：跟随派活者/全局（null），不编一个名字出来</summary>
    [Fact]
    public void NothingUsable_FollowsTheParent()
    {
        AgentSettingConfig config = new() { GeneralSubAgentModelNames = { "dead" } };

        ModelChoice pick = SubAgentModelChooser.Pick(SubAgentProfile.General, null, config,
            new FakeProbe("a"));

        Assert.Null(pick.Name);
        Assert.Equal(string.Empty, pick.Notice);
    }

    /// <summary>各档读各自那一列：通用档配的模型不该被脑拿去用</summary>
    [Fact]
    public void EachProfileReadsItsOwnColumn()
    {
        AgentSettingConfig config = new()
        {
            GeneralSubAgentModelNames = { "general-model" },
            BrainSubAgentModelNames = { "brain-model" },
        };
        FakeProbe probe = new("general-model", "brain-model");

        Assert.Equal("brain-model",
            SubAgentModelChooser.Pick(SubAgentProfile.Brain, null, config, probe).Name);
        Assert.Equal("general-model",
            SubAgentModelChooser.Pick(SubAgentProfile.General, null, config, probe).Name);
    }

    private sealed class FakeProbe(params string[] pinnable) : ISubAgentModelPinProbe
    {
        private readonly string[] _pinnable = pinnable; //模拟「跑得起来」的那几个

        public List<string> Known { get; } = [..pinnable]; //模拟模型库里有名字

        public bool Knows(string name) => Known.Contains(name);

        public string? Pin(string? name) => name != null && _pinnable.Contains(name) ? name : null;
    }
}
