using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Features.Settings;

namespace UiharuMind.App.Tests.Settings;

/// <summary>
/// 钉死设置页的技能分组。生态里的技能包普遍是「包名/skills/分类/技能名」，
/// 只按包名分会得到一个几十条的大组，等于没分。
/// </summary>
public class SkillGroupingTests
{
    private static SkillCatalogEntry Entry(string relativePath)
    {
        return new SkillCatalogEntry
        {
            Name = relativePath[(relativePath.LastIndexOf('/') + 1)..],
            RelativePath = relativePath,
        };
    }

    [Fact]
    public void Build_SplitsPackageAndCategory()
    {
        List<SkillGroupItem> groups = SkillGrouping.Build([
            Entry("matt/skills/engineering/tdd"),
            Entry("matt/skills/productivity/teach"),
            Entry("matt/skills/engineering/research"),
        ]);

        SkillGroupItem group = Assert.Single(groups);
        Assert.Equal("matt", group.Name);
        Assert.Equal(3, group.SkillCount);
        Assert.Equal(["engineering", "productivity"], group.Categories.Select(x => x.Name));
        Assert.Equal(["research", "tdd"], group.Categories[0].Skills.Select(x => x.Name));
    }

    [Fact]
    public void Build_PutsRootLevelSkillsInTheLocalGroupFirst()
    {
        List<SkillGroupItem> groups = SkillGrouping.Build([
            Entry("aaa-pack/skills/misc/one"),
            Entry("my-own-skill"),
        ]);

        // 本地技能置顶,哪怕包名的字典序在它前面
        Assert.True(groups[0].IsLocal);
        Assert.Equal(["my-own-skill"], groups[0].Categories.Single().Skills.Select(x => x.Name));
        Assert.False(groups[0].Categories.Single().HasName); //根下技能没有分类层,不该造一个空组名
    }

    [Fact]
    public void Build_KeepsUncategorizedSkillsAheadOfCategories()
    {
        List<SkillGroupItem> groups = SkillGrouping.Build([
            Entry("pack/skills/engineering/tdd"),
            Entry("pack/loose"),
        ]);

        SkillGroupItem group = Assert.Single(groups);
        Assert.False(group.Categories[0].HasName);
        Assert.Equal(["loose"], group.Categories[0].Skills.Select(x => x.Name));
        Assert.Equal("engineering", group.Categories[1].Name);
    }

    [Fact]
    public void Build_TellsDuplicateNameApartFromSpecFailure()
    {
        List<SkillGroupItem> groups = SkillGrouping.Build([
            new SkillCatalogEntry
            {
                Name = "tdd", RelativePath = "pack/dup",
                LoadState = ESkillLoadState.DuplicateName, DuplicateOfPath = "/skills/other/tdd",
            },
            new SkillCatalogEntry { Name = "bad", RelativePath = "pack/bad", LoadState = ESkillLoadState.Invalid },
        ]);

        List<SkillDisplayItem> skills = groups.Single().Categories.Single().Skills.ToList();
        Assert.All(skills, x => Assert.False(x.IsLoaded));
        Assert.Contains("/skills/other/tdd", skills.Single(x => x.Name == "tdd").LoadFailureHint);
        Assert.NotEqual(skills[0].LoadFailureHint, skills[1].LoadFailureHint);
    }
}
