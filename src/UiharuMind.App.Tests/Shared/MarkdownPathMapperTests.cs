using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 回复正文里的简写与相对路径按会话解析（ADR 0080）；图片经哨兵基准拼出的路径能还原，写了绝对地址的旧消息原样用
/// </summary>
public sealed class MarkdownPathMapperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-md-paths-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Workspace => Path.Combine(_root, "workspace");

    private string Draft => Path.Combine(_root, "draft");

    private MarkdownPathMapper Mapper => MarkdownPathMapper.Create(new AgentPathResolver(Workspace, Draft))!;

    /// 渲染库对相对图片地址的做法：与基准拼接后取全路径
    private string AsLibraryCombines(string link) => Path.GetFullPath(Path.Combine(Mapper.ImageBasePath, link));

    [Fact]
    public void ResolveLink_ExpandsDraftShorthand_AndWorkspaceRelative()
    {
        Assert.Equal(Path.Combine(Draft, "a.png"), Mapper.ResolveLink("$DRAFT/a.png"));
        Assert.Equal(Path.Combine(Workspace, "src", "a.cs"), Mapper.ResolveLink("src/a.cs#L3"));
    }

    [Fact]
    public void ResolveLink_ReturnsNull_ForPureAnchor()
    {
        Assert.Null(Mapper.ResolveLink("#section"));
    }

    [Fact]
    public void MapLocalPath_RestoresLinksCombinedWithTheSentinel()
    {
        Assert.Equal(Path.Combine(Draft, "charts", "a.png"), Mapper.MapLocalPath(AsLibraryCombines("$DRAFT/charts/a.png")));
        Assert.Equal(Path.Combine(Workspace, "docs", "x.png"), Mapper.MapLocalPath(AsLibraryCombines("docs/x.png")));
    }

    [Fact]
    public void MapLocalPath_KeepsExistingAbsolutePaths()
    {
        string old = Path.Combine(Draft, "old.png");
        Directory.CreateDirectory(Draft);
        File.WriteAllBytes(old, [1]);

        Assert.Equal(old, Mapper.MapLocalPath(old));
    }

    [Fact]
    public void Create_ReturnsNull_WithoutAnyRoot()
    {
        Assert.Null(MarkdownPathMapper.Create(new AgentPathResolver(null)));
    }
}
