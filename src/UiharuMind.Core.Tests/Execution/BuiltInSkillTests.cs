using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools.Skills;
using UiharuMind.Core.AI.Execution.Skills;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 内置技能（ADR 0061）：正文每次读取现生成；可用与否随时会变，叠在文件技能之上、同名让位给文件技能
/// </summary>
public class BuiltInSkillTests
{
    private static AgentSkill File(string name) => new AgentInlineSkill(name, $"description of {name}", $"{name} file body");

    private static async Task<IList<AgentSkill>> List(AgentSkillsSource source) =>
        await source.GetSkillsAsync(null!, TestContext.Current.CancellationToken);

    [Fact]
    public async Task BuiltIns_FollowFileSkills()
    {
        BuiltInSkill guide = new("uiharu-guide", "guide", () => "guide body");
        using BuiltInSkillsSource source = new(new AgentInMemorySkillsSource([File("tdd")]), () => [guide]);

        Assert.Equal(["tdd", "uiharu-guide"], (await List(source)).Select(x => x.Frontmatter.Name));
    }

    /// <summary>开发者模式运行中开关：不重新扫盘、不作废缓存，下一次列举就跟上</summary>
    [Fact]
    public async Task Availability_IsReadOnEveryListing()
    {
        bool enabled = false;
        BuiltInSkill dev = new("uiharu-dev", "dev", () => "dev body", () => enabled);
        using BuiltInSkillsSource source = new(new AgentInMemorySkillsSource([]), () => [dev]);

        Assert.Empty(await List(source));
        enabled = true;
        Assert.Equal("uiharu-dev", Assert.Single(await List(source)).Frontmatter.Name);
    }

    /// <summary>用户自己放一个同名技能，就顶掉内置的那份</summary>
    [Fact]
    public async Task FileSkillWithTheSameName_Wins()
    {
        BuiltInSkill guide = new("uiharu-guide", "guide", () => "built-in body");
        using BuiltInSkillsSource source = new(new AgentInMemorySkillsSource([File("uiharu-guide")]), () => [guide]);

        AgentSkill skill = Assert.Single(await List(source));
        Assert.Contains("file body", await skill.GetContentAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>扫盘失败不连累内置：内置不依赖磁盘，目录里至少还有它们</summary>
    [Fact]
    public async Task FileSourceFailure_StillServesBuiltIns()
    {
        BuiltInSkill guide = new("uiharu-guide", "guide", () => "guide body");
        using BuiltInSkillsSource source = new(new ThrowingSkillsSource(), () => [guide]);

        Assert.Equal("uiharu-guide", Assert.Single(await List(source)).Frontmatter.Name);
    }

    private sealed class ThrowingSkillsSource : AgentSkillsSource
    {
        public override Task<IList<AgentSkill>> GetSkillsAsync(AgentSkillsSourceContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("disk gone");
    }

    /// <summary>正文取读取那一刻的事实，而且同步完成（目录的过滤谓词在同步上下文里读原文）</summary>
    [Fact]
    public void Content_IsBuiltOnEveryRead_Synchronously()
    {
        int reads = 0;
        BuiltInSkill skill = new("uiharu-dev", "dev", () => $"read {++reads}");

        ValueTask<string> first = skill.GetContentAsync(TestContext.Current.CancellationToken);
        ValueTask<string> second = skill.GetContentAsync(TestContext.Current.CancellationToken);

        Assert.True(first.IsCompletedSuccessfully);
        Assert.Equal("read 1", first.Result);
        Assert.Equal("read 2", second.Result);
        Assert.True(SkillCatalog.IsModelInvocable(skill)); //没有 frontmatter 声明，照常参与模型自选
        Assert.Equal(2, reads); //是否参与自选不读正文，否则每次列举都要付一次全文生成
    }

    /// <summary>用户放了同名文件技能，内置让位：名单里也不出现，否则描述与实际加载到的正文对不上</summary>
    [Fact]
    public void ShadowedBuiltIns_AreHiddenFromTheList()
    {
        BuiltInSkill guide = new("uiharu-guide", "guide", () => "built-in body");
        BuiltInSkill dev = new("uiharu-dev", "dev", () => "dev body");

        Assert.Equal(["uiharu-dev"],
            SkillCatalog.SubtractShadowed([guide, dev], ["Uiharu-Guide"]).Select(x => x.Frontmatter.Name));
    }

    /// <summary>技能清单不发给模型时，内置技能的名字只能从 Skill 工具描述里得知</summary>
    [Fact]
    public void SkillTool_NamesTheBuiltIns()
    {
        BuiltInSkill guide = new("uiharu-guide", "How to use this app.", () => "body");

        AIFunction plain = (AIFunction)LoadSkillTool.Create(new AgentInMemorySkillsSource([]), true, true);
        AIFunction withBuiltIns = (AIFunction)LoadSkillTool.Create(new AgentInMemorySkillsSource([]), true, true, [guide]);

        Assert.DoesNotContain("uiharu-guide", plain.Description);
        Assert.Contains("- uiharu-guide: How to use this app.", withBuiltIns.Description);
    }

    /// <summary>多行与超长描述压成一行：工具描述是一行一条，不能被描述里的换行撕开</summary>
    [Fact]
    public void SkillTool_SingleLinesAndTruncatesTheDescriptions()
    {
        BuiltInSkill multi = new("multi", "one\ntwo", () => "body");
        BuiltInSkill huge = new("huge", new string('x', 300), () => "body");
        AIFunction tool = (AIFunction)LoadSkillTool.Create(new AgentInMemorySkillsSource([]), true, true, [multi, huge]);

        Assert.Contains("- multi: one two", tool.Description);
        Assert.Contains("- huge: " + new string('x', 200) + "…", tool.Description);
    }

    /// <summary>开发者模式一开关，可用的内置技能就变：清单关着时要重建，开着时框架每轮现取、不必重建</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void BuiltInChanges_RebuildOnlyWhenTheListIsOff(bool modelSkillsEnabled, bool rebuilds)
    {
        CharacterData character = new() { CharacterId = "x", IsAgent = true };
        AgentAssemblyFacts Capture(string builtIns) => AgentAssemblyFacts.Capture(character, new AgentAssemblyInputs
        {
            IsAgentForm = true, ModelSkillsEnabled = modelSkillsEnabled, BuiltInSkills = builtIns,
        });

        Assert.Equal(rebuilds, Capture("uiharu-guide") != Capture("uiharu-guide\nuiharu-dev"));
    }
}
