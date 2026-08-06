using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Character;

/// <summary>
/// 片段库的内置预设。它是嵌入资源，资源名或结构一错就是静默失效——
/// 用户看到的是一个空片段库，没有任何报错。
/// </summary>
public class PromptSnippetTests
{
    [Fact]
    public void SeedResource_ParsesWithNameAndText()
    {
        List<PromptSnippet> seed =
            EmbeddedResourcesUtils.ReadFromJson<List<PromptSnippet>>("PromptSnippets.json");

        Assert.NotEmpty(seed);
        Assert.All(seed, snippet =>
        {
            Assert.False(string.IsNullOrWhiteSpace(snippet.Name));
            Assert.False(string.IsNullOrWhiteSpace(snippet.Text));
        });
    }

    [Fact]
    public void SeedResource_CarriesTheRoleplayScaffolds()
    {
        List<PromptSnippet> seed =
            EmbeddedResourcesUtils.ReadFromJson<List<PromptSnippet>>("PromptSnippets.json");

        // 这两段原是 Roleplay_FirstPerson / Roleplay_ThirdPerson 两个内置角色，
        // 退出角色域后必须仍能从片段库拿到，否则新建扮演角色就只剩白纸
        Assert.Contains(seed, x => x.Text.Contains("第一人称角色扮演系统", StringComparison.Ordinal));
        Assert.Contains(seed, x => x.Text.Contains("第三人称角色扮演系统", StringComparison.Ordinal));
    }
}
