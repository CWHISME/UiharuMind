using System.Text.Json;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests.Configs;

/// <summary>
/// 子代理模型从「一档一个」改成「一档多个」的读档迁移（ADR 0111）。
///
/// 这段必须钉住，因为它的失败是<b>静默</b>的：不迁移，旧档里的模型配置就原地失效——
/// 列表默认空，界面显示「跟随主代理」，用户看不出是升级弄丢的，只会觉得子代理突然变慢。
///
/// <b>「<c>Current</c> 的静态初始化有没有调钩子」这件事在这里量不到</b>：读档只走源生成的
/// <c>SaveJsonContext</c>（未登记的探针类型根本读不出来），而静态初始化一辈子只跑一次，
/// 测试进程里没法把它重放一遍。能做到的是把钩子里那一半钉死（override 在、迁移对、幂等），
/// 以及「钩子抛了也不把配置废掉」；接线本身靠 ADR 0111 与手工核一次旧档。
/// </summary>
public class AgentSettingConfigMigrationTests
{
    /// <summary>旧档只有单值字段：迁移后进列表，旧字段清空</summary>
    [Fact]
    public void LegacySingleValue_MovesIntoTheList()
    {
        AgentSettingConfig config = SaveUtility.LoadFromString<AgentSettingConfig>("""
            {
              "GeneralSubAgentModelName": "main-1",
              "BrainSubAgentModelName": "fast-1",
              "ExplorerSubAgentModelName": "survey-1"
            }
            """);

        config.MigrateLegacySubAgentModels();

        Assert.Equal(new[] { "main-1" }, config.GeneralSubAgentModelNames);
        Assert.Equal(new[] { "fast-1" }, config.BrainSubAgentModelNames);
        Assert.Equal(new[] { "survey-1" }, config.ExplorerSubAgentModelNames);
        Assert.Null(config.LegacyGeneralSubAgentModelName);
    }

    /// <summary>新档的列表原样保留，迁移只补旧的、不覆盖新的</summary>
    [Fact]
    public void ExistingList_IsKeptAsIs()
    {
        AgentSettingConfig config = SaveUtility.LoadFromString<AgentSettingConfig>("""
            {
              "GeneralSubAgentModelNames": ["a", "b"],
              "GeneralSubAgentModelName": "legacy"
            }
            """);

        config.MigrateLegacySubAgentModels();

        Assert.Equal(new[] { "a", "b", "legacy" }, config.GeneralSubAgentModelNames);
    }

    /// <summary>旧字段为空串/缺失都等于没配：不该在列表里塞一条空模型名</summary>
    [Fact]
    public void BlankLegacy_AddsNothing()
    {
        AgentSettingConfig config = SaveUtility.LoadFromString<AgentSettingConfig>("""
            { "BrainSubAgentModelName": "   " }
            """);

        config.MigrateLegacySubAgentModels();

        Assert.Empty(config.BrainSubAgentModelNames);
    }

    /// <summary>
    /// 手改的档里写着 <c>null</c> 也不能把 null 递到派活侧：那里会拿它直接轮换，一抳就是空引用。
    /// 读档入口把它当空列表
    /// </summary>
    [Fact]
    public void NullListInFile_ReadsAsEmpty()
    {
        AgentSettingConfig config = SaveUtility.LoadFromString<AgentSettingConfig>("""
            { "GeneralSubAgentModelNames": null }
            """);

        Assert.NotNull(config.GeneralSubAgentModelNames);
        Assert.Empty(config.GeneralSubAgentModelNames);
    }

    /// <summary>
    /// 迁移<b>幂等</b>：老档在用户下次改设置之前一直是旧形状，所以每次启动都会再跑一遍。
    /// 去重靠 <c>Contains</c>，重跑不能变成 ["b", "b"]。
    /// </summary>
    [Fact]
    public void Migration_IsIdempotent()
    {
        AgentSettingConfig config = new() { LegacyBrainSubAgentModelName = "b" };

        config.MigrateLegacySubAgentModels();
        config.MigrateLegacySubAgentModels();

        Assert.Equal(new[] { "b" }, config.BrainSubAgentModelNames);
    }

    /// <summary>
    /// 迁移真的挂在读档钩子上。删掉 <see cref="AgentSettingConfig.OnLoaded"/> 那个 override，
    /// 这条会红——而其它手调迁移函数的用例照样绿，换句话说它们证明不了钩子通不通。
    /// </summary>
    [Fact]
    public void OnLoaded_MigratesLegacyFields()
    {
        AgentSettingConfig config = new() { LegacyGeneralSubAgentModelName = "g" };

        config.OnLoaded();

        Assert.Equal(new[] { "g" }, config.GeneralSubAgentModelNames);
        Assert.Null(config.LegacyGeneralSubAgentModelName);
    }

    /// <summary>
    /// 迁移完落盘时旧字段必须消失：留着空串会让人以为「这里还能手填」，
    /// 而实际生效的已经是列表——两份真相迟早对不上。
    /// </summary>
    [Fact]
    public void MigratedField_IsNotWrittenBack()
    {
        AgentSettingConfig config = SaveUtility.LoadFromString<AgentSettingConfig>("""
            { "GeneralSubAgentModelName": "main-1" }
            """);
        config.MigrateLegacySubAgentModels();

        using JsonDocument saved = JsonDocument.Parse(SaveUtility.SaveToString(config));

        Assert.False(saved.RootElement.TryGetProperty("GeneralSubAgentModelName", out _));
        Assert.True(saved.RootElement.TryGetProperty("GeneralSubAgentModelNames", out JsonElement names));
        Assert.Equal("main-1", Assert.Single(names.EnumerateArray()).GetString());
    }

    /// <summary>钩子抛异常不能把整份配置废掉：类型初始化抛一次，之后每次访问 <c>Current</c> 都炸</summary>
    [Fact]
    public void HookFailure_KeepsTheConfigUsable()
    {
        Assert.NotNull(ThrowingProbeConfig.Current);
        Assert.NotNull(ThrowingProbeConfig.Current); // 第二次是真的走一遍静态字段的读
    }

    /// <summary>钩子一抛就不能影响后续访问</summary>
    private sealed class ThrowingProbeConfig : TConfigBase<ThrowingProbeConfig>
    {
        internal override void OnLoaded() => throw new InvalidOperationException("probe boom");
    }
}
