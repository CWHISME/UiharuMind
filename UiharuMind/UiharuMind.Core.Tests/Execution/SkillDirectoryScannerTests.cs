using UiharuMind.Core.AI.Execution.Skills;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死深层扫描:框架的 AgentFileSkillsSource 搜索深度是常量 2(SearchDepth 选项传再大也被 clamp),
/// 而生态里的技能包普遍是「包名/skills/分类/技能名」四层。这里跑偏就是整包技能静默消失。
/// </summary>
public class SkillDirectoryScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-skill-scan-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private void WriteSkill(string relativePath)
    {
        string directory = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), "---\nname: x\ndescription: y\n---\n\nbody\n");
    }

    private async Task<List<string>> ScanAsync()
    {
        List<SkillDirectory> found = await new SkillDirectoryScanner().ScanAsync(_root);
        return found.Select(x => x.RelativePath).ToList();
    }

    [Fact]
    public async Task Scan_FindsSkillsBelowTheFrameworkDepthLimit()
    {
        WriteSkill("local-skill");
        WriteSkill("pack/wrapped");
        WriteSkill("pack/skills/productivity/grilling");
        WriteSkill("pack/skills/a/b/c/deep");

        Assert.Equal(
            ["local-skill", "pack/skills/a/b/c/deep", "pack/skills/productivity/grilling", "pack/wrapped"],
            await ScanAsync());
    }

    [Fact]
    public async Task Scan_KeepsOnlyTheOutermostSkill()
    {
        // 教人写技能的技能会把示例 SKILL.md 当附件放在自己目录里,滤不掉就会冒出一堆假技能
        WriteSkill("pack/writing-skills");
        WriteSkill("pack/writing-skills/examples/sample");

        Assert.Equal(["pack/writing-skills"], await ScanAsync());
    }

    [Fact]
    public async Task Scan_IgnoresSkillFileAtTheRootItself()
    {
        // 根成了技能,它的父目录就会被当成来源根,连带扫到根的兄弟目录
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(_root).FullName, "SKILL.md"), "---\nname: root\n---\n");
        WriteSkill("pack/ok");

        Assert.Equal(["pack/ok"], await ScanAsync());
    }

    [Fact]
    public async Task Scan_SkipsHardExcludedDirectories()
    {
        WriteSkill("pack/node_modules/junk");
        WriteSkill("pack/real");

        Assert.Equal(["pack/real"], await ScanAsync());
    }

    [Fact]
    public async Task Scan_ReturnsEmptyWhenRootMissing()
    {
        Assert.Empty(await new SkillDirectoryScanner().ScanAsync(Path.Combine(_root, "nope")));
    }
}
