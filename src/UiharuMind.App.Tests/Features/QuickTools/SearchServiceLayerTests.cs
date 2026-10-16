using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Features.QuickTools;

namespace UiharuMind.App.Tests.Features.QuickTools;

/// <summary>
/// 浏览层（列当前目录一层）的显示隐藏开关：直调 <see cref="Directory"/> 单层枚举，
/// 不走搜索器——开关只管点开头/Hidden 条目出不出列。
/// </summary>
public class SearchServiceLayerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-layer-" + Guid.NewGuid().ToString("N"));

    public SearchServiceLayerTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "kept.txt"), "x");
        File.WriteAllText(Path.Combine(_root, ".hidden.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        Directory.CreateDirectory(Path.Combine(_root, ".hiddendir"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public async Task ListLayer_HidesDotEntriesByDefault()
    {
        LayerListing layer = await new SearchService()
            .ListLayerAsync(_root, TestContext.Current.CancellationToken);

        Assert.Null(layer.Failure);
        Assert.Equal(["sub", "kept.txt"], layer.Items.Select(i => i.FileName));
        Assert.True(layer.Items[0].IsDirectory);
        Assert.False(layer.Items[1].IsDirectory);
    }

    [Fact]
    public async Task ListLayer_ShowsDotEntriesWhenToggled()
    {
        LayerListing layer = await new SearchService()
            .ListLayerAsync(_root, TestContext.Current.CancellationToken, includeHidden: true);

        Assert.Null(layer.Failure);
        Assert.Equal([".hiddendir", "sub", ".hidden.txt", "kept.txt"], layer.Items.Select(i => i.FileName));
        Assert.Equal([true, false, true, false], layer.Items.Select(i => i.IsHidden));
    }

    [Fact]
    public async Task ListLayer_MissingDirectory_ReportsPathNotFound()
    {
        LayerListing layer = await new SearchService()
            .ListLayerAsync(Path.Combine(_root, "nope"), TestContext.Current.CancellationToken);

        Assert.Equal(ESearchFailureKind.PathNotFound, layer.Failure?.Kind);
        Assert.Empty(layer.Items);
    }
}
