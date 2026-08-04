using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Skills;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 点名调用的语法规则。<c>/技能名 参数</c> 是用户唯一的显式入口，
/// 解析跑偏会静默退化成"把整行当普通消息发出去"，实机上很难归因。
/// </summary>
public class SkillInvocationParseTests
{
    [Theory]
    [InlineData("/git-auto-commit", "git-auto-commit", "")]
    [InlineData("/git-auto-commit 半小时后提交", "git-auto-commit", "半小时后提交")]
    [InlineData("/name   前后空白都要吃掉   ", "name", "前后空白都要吃掉")]
    [InlineData("/name\n换行也算参数分隔", "name", "换行也算参数分隔")]
    public void TryParse_AcceptsNamedInvocation(string text, string expectedName, string expectedArguments)
    {
        Assert.True(SkillInvocation.TryParse(text, out string skillName, out string arguments));
        Assert.Equal(expectedName, skillName);
        Assert.Equal(expectedArguments, arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")] //只有斜杠不是点名
    [InlineData("/ name")] //斜杠后直接空白,技能名为空
    [InlineData("普通消息")]
    [InlineData("说明一下 /usr/bin 这个路径")] //斜杠不在行首
    public void TryParse_RejectsPlainText(string text)
    {
        Assert.False(SkillInvocation.TryParse(text, out _, out _));
    }

    [Theory]
    [InlineData("/", "")]
    [InlineData("/git", "git")]
    public void TryParsePrefix_OpensWhileNameIsBeingTyped(string text, string expectedPrefix)
    {
        Assert.True(SkillInvocation.TryParsePrefix(text, out string prefix));
        Assert.Equal(expectedPrefix, prefix);
    }

    [Theory]
    [InlineData("/git ")] //敲下空格即开始写参数,补全应收起
    [InlineData("/git\n")]
    [InlineData("普通消息")]
    [InlineData("")]
    public void TryParsePrefix_ClosesOnceArgumentsStart(string text)
    {
        Assert.False(SkillInvocation.TryParsePrefix(text, out _));
    }
}

/// <summary>
/// 技能是否参与模型自选。<see cref="SkillCatalog.DisableModelInvocationKey"/> 有两种落位：
/// <b>顶层</b>（Claude Code 与其技能市场一律这么写）与 <c>metadata:</c> 块（框架原生能解析的位置）。
/// 框架解析器静默丢弃未知顶层键，所以顶层那条必须由我们自己从原文里捞——
/// 漏掉它意味着别处拿来的「仅点名」技能会照常被模型自选，且不报任何错。
/// </summary>
public class SkillModelInvocationTests
{
    [Fact]
    public void IsModelInvocable_TrueWhenMarkerAbsent()
    {
        Assert.True(SkillCatalog.IsModelInvocable(CreateSkill(null)));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData(" true ")] //框架把 metadata 值一律当字符串取,这里容忍空白
    public void IsModelInvocable_FalseWhenMetadataMarkerSet(string value)
    {
        Assert.False(SkillCatalog.IsModelInvocable(CreateSkill(value)));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    public void IsModelInvocable_TrueWhenMetadataMarkerNotTrue(string value)
    {
        Assert.True(SkillCatalog.IsModelInvocable(CreateSkill(value)));
    }

    [Theory]
    [InlineData("disable-model-invocation: true", "true")]
    [InlineData("disable-model-invocation: True", "True")]
    [InlineData("disable-model-invocation:   true  ", "true")]
    [InlineData("disable-model-invocation: \"true\"", "true")]
    [InlineData("disable-model-invocation: 'true'", "true")]
    public void ReadTopLevelFrontmatterValue_ReadsMarkerWrittenAtTopLevel(string line, string expected)
    {
        Assert.Equal(expected, ReadMarker(line));
    }

    [Theory]
    [InlineData("disable-model-invocation: false", "false")]
    [InlineData("allowed-tools: run_shell", null)] //别的顶层键不该被误读
    [InlineData("", null)]
    [InlineData("  disable-model-invocation: true", null)] //缩进行属嵌套块,不是顶层声明
    [InlineData("metadata:\n  disable-model-invocation: true", null)]
    public void ReadTopLevelFrontmatterValue_IgnoresEverythingElse(string line, string? expected)
    {
        Assert.Equal(expected, ReadMarker(line));
    }

    [Fact]
    public void ReadTopLevelFrontmatterValue_IgnoresBodyOutsideFrontmatter()
    {
        // 正文里出现同名的一行不算声明,否则讲解这个键的技能会把自己关掉
        string content = "---\nname: demo-skill\ndescription: d\n---\n\ndisable-model-invocation: true\n";
        Assert.Null(SkillCatalog.ReadTopLevelFrontmatterValue(content, SkillCatalog.DisableModelInvocationKey));
    }

    private static string? ReadMarker(string extraFrontmatterLine)
    {
        string content = $"---\nname: demo-skill\ndescription: d\n{extraFrontmatterLine}\n---\n\nBody.";
        return SkillCatalog.ReadTopLevelFrontmatterValue(content, SkillCatalog.DisableModelInvocationKey);
    }

    [Fact]
    public void IsAdvertised_ExcludesDisabledSkill()
    {
        HashSet<string> disabled = new(["demo-skill"], StringComparer.OrdinalIgnoreCase);
        Assert.False(SkillCatalog.IsAdvertised(CreateSkill(null), disabled));
    }

    [Fact]
    public void IsAdvertised_ExcludesSkillThatOptedOutOfModelInvocation()
    {
        Assert.False(SkillCatalog.IsAdvertised(CreateSkill("true"), new HashSet<string>()));
    }

    [Fact]
    public void IsAdvertised_IncludesEnabledModelInvocableSkill()
    {
        Assert.True(SkillCatalog.IsAdvertised(CreateSkill(null), new HashSet<string>()));
    }

    private static AgentSkill CreateSkill(string? markerValue)
    {
        AgentSkillFrontmatter frontmatter = new("demo-skill", "A skill used by the tests.");
        if (markerValue != null)
        {
            frontmatter.Metadata = new AdditionalPropertiesDictionary
            {
                [SkillCatalog.DisableModelInvocationKey] = markerValue,
            };
        }

        return new AgentInlineSkill(frontmatter, "Body.");
    }
}

/// <summary>
/// 端到端钉住 Claude Code 写法：技能市场上的 SKILL.md 一律把
/// <c>disable-model-invocation</c> 写在<b>顶层</b>，而框架解析器静默丢弃未知顶层键。
/// 若只读 <c>metadata:</c>，别处拿来的「仅点名」技能会照常进广告列表被模型自选，
/// 且没有任何报错——所以这条要用真实文件 + 真实框架加载来验。
/// </summary>
public class TopLevelMarkerEndToEndTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"uiharu-skills-{Guid.NewGuid():N}");

    [Fact]
    public async Task FrameworkDropsTopLevelMarker_ButCatalogStillHonorsIt()
    {
        AgentSkill skill = await LoadSingleSkillAsync("""
                                                     ---
                                                     name: user-only-skill
                                                     description: A skill only the user may invoke.
                                                     disable-model-invocation: true
                                                     ---

                                                     Do the thing.
                                                     """);

        // 前提:框架确实没把顶层键收进 Frontmatter(这条一旦变红,说明框架开始支持了,可以简化实现)
        Assert.True(skill.Frontmatter.Metadata == null ||
                    !skill.Frontmatter.Metadata.ContainsKey(SkillCatalog.DisableModelInvocationKey));

        Assert.False(SkillCatalog.IsModelInvocable(skill));
        Assert.False(SkillCatalog.IsAdvertised(skill, new HashSet<string>()));
    }

    [Fact]
    public async Task SkillWithoutMarkerStaysModelInvocable()
    {
        AgentSkill skill = await LoadSingleSkillAsync("""
                                                     ---
                                                     name: user-only-skill
                                                     description: An ordinary skill.
                                                     ---

                                                     Do the thing.
                                                     """);

        Assert.True(SkillCatalog.IsModelInvocable(skill));
        Assert.True(SkillCatalog.IsAdvertised(skill, new HashSet<string>()));
    }

    private async Task<AgentSkill> LoadSingleSkillAsync(string skillFileContent)
    {
        // 技能名必须等于目录名,否则框架整个拒绝加载
        string directory = Path.Combine(_root, "user-only-skill");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "SKILL.md"), skillFileContent);

        AgentFileSkillsSource source = new(_root);
        IList<AgentSkill> skills = await source.GetSkillsAsync(null!);
        return Assert.Single(skills);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

/// <summary>
/// 注入正文要剔掉 frontmatter：name/description 只为进广告列表而存，对模型是冗余 token。
/// </summary>
public class SkillFrontmatterStrippingTests
{
    [Fact]
    public void StripFrontmatter_RemovesLeadingYamlBlock()
    {
        string content = "---\nname: demo\ndescription: d\n---\n\n# Demo\n\nDo the thing.";
        Assert.Equal("# Demo\n\nDo the thing.", SkillCatalog.StripFrontmatter(content));
    }

    [Fact]
    public void StripFrontmatter_KeepsBodyWithoutFrontmatter()
    {
        Assert.Equal("# Demo", SkillCatalog.StripFrontmatter("# Demo\n"));
    }

    [Fact]
    public void StripFrontmatter_KeepsContentWhenBlockIsUnterminated()
    {
        string content = "---\nname: demo";
        Assert.Equal(content, SkillCatalog.StripFrontmatter(content));
    }

    [Fact]
    public void StripFrontmatter_ToleratesBomAndCrlf()
    {
        string content = "﻿---\r\nname: demo\r\n---\r\n\r\nBody.";
        Assert.Equal("Body.", SkillCatalog.StripFrontmatter(content));
    }
}

/// <summary>
/// 注入正文里交代附件/脚本怎么取的那一行，必须随实际启用的工具集裁剪。
/// 提一个门控关掉的工具，就是在指挥模型去调不存在的东西——
/// 这正是被删掉那个示范技能的毛病（它无条件教模型调 create_scheduled_task）。
/// </summary>
public class SkillResourceAccessLineTests
{
    [Fact]
    public void MentionsBothToolSets_WhenFileAndShellAreOn()
    {
        string line = SkillCatalog.BuildResourceAccessLine(true, true, isModelInvocable: true);

        Assert.Contains("your file and shell tools", line);
        Assert.Contains("run its scripts", line);
    }

    [Fact]
    public void OmitsScripts_WhenShellIsOff()
    {
        string line = SkillCatalog.BuildResourceAccessLine(true, false, isModelInvocable: true);

        Assert.Contains("your file tools", line);
        Assert.DoesNotContain("run its scripts", line);
        Assert.DoesNotContain("run_shell", line);
    }

    [Fact]
    public void NamesShellTool_WhenOnlyShellIsOn()
    {
        string line = SkillCatalog.BuildResourceAccessLine(false, true, isModelInvocable: true);

        Assert.Contains("run_shell", line);
        Assert.DoesNotContain("file tools", line);
    }

    [Fact]
    public void PromisesNothing_WhenBothAreOff()
    {
        // 两样都关时附件与脚本确实取不到,不能承诺一条不存在的路
        Assert.Equal(string.Empty, SkillCatalog.BuildResourceAccessLine(false, false, isModelInvocable: true));
    }

    [Fact]
    public void WarnsAboutSkillTools_OnlyForSkillsTheyCannotReach()
    {
        // 退出模型自选的技能不在框架 source 列表里,技能工具对它一律 not found
        Assert.Contains("skill tools cannot reach",
            SkillCatalog.BuildResourceAccessLine(true, true, isModelInvocable: false));

        // 仍参与自选的技能反过来——那几个工具好用,不该拦
        Assert.DoesNotContain("skill tools cannot reach",
            SkillCatalog.BuildResourceAccessLine(true, true, isModelInvocable: true));
    }
}

/// <summary>
/// 点名调用的整套设计压在一个前提上：消息的 <c>AdditionalProperties</c> 标记能跟着会话历史落盘往返。
/// 且往返后值不再是 string 而是 <see cref="System.Text.Json.JsonElement"/>——
/// 读取端强转 string 会在"重开会话"这个最普通的路径上悄悄失效，气泡从此显示整段技能正文。
/// </summary>
public class SkillInvocationMarkerRoundTripTests
{
    [Fact]
    public void NamedSkillMarker_SurvivesSessionSerialization()
    {
        ChatMessage message = new(ChatRole.User, "注入的技能正文")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.NamedSkill] = "git-auto-commit",
                [ChatMessageAnnotations.NamedSkillInput] = "/git-auto-commit 半小时后提交",
            },
        };

        string json = System.Text.Json.JsonSerializer.Serialize(message, SessionJsonOptions.Default);
        ChatMessage restored =
            System.Text.Json.JsonSerializer.Deserialize<ChatMessage>(json, SessionJsonOptions.Default)!;

        Assert.True(restored.AdditionalProperties!.TryGetValue(ChatMessageAnnotations.NamedSkillInput, out object? input));
        Assert.IsNotType<string>(input); //钉住"往返后不再是 string"这条事实,读取端必须走 ToString
        Assert.Equal("/git-auto-commit 半小时后提交", input?.ToString());
        Assert.True(restored.AdditionalProperties.TryGetValue(ChatMessageAnnotations.NamedSkill, out object? name));
        Assert.Equal("git-auto-commit", name?.ToString());
        Assert.Equal("注入的技能正文", restored.Text);
    }
}
