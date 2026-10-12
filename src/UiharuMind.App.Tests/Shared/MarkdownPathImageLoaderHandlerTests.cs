using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 图片地址来自不可信正文：缺文件、无权限、超长路径都只让这张图加载失败，
/// LoadAsync 必须返回 faulted Task，不能同步抛逃逸给库的图片管线
/// </summary>
public sealed class MarkdownPathImageLoaderHandlerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-md-img-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsFaultedTaskInsteadOfThrowing()
    {
        string workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(workspace); //目录存在、文件缺失：稳定抛 FileNotFoundException
        var mapper = MarkdownPathMapper.Create(
            new AgentPathResolver(workspace, Path.Combine(_root, "draft")))!;
        var handler = new MarkdownPathImageLoaderHandler(mapper);
        string combined = Path.GetFullPath(Path.Combine(mapper.ImageBasePath, "missing.png"));

        Task<Stream>? task = null;
        Exception? syncThrow = null;
        try
        {
            task = handler.LoadAsync(new Uri(combined), CancellationToken.None);
        }
        catch (Exception e)
        {
            syncThrow = e;
        }

        Assert.Null(syncThrow);
        await Assert.ThrowsAsync<FileNotFoundException>(() => task!);
    }

    [Fact]
    public async Task LoadAsync_ExistingFile_LoadsContent()
    {
        string draft = Path.Combine(_root, "draft");
        Directory.CreateDirectory(draft);
        string file = Path.Combine(draft, "a.png");
        await File.WriteAllBytesAsync(file, [1, 2, 3], TestContext.Current.CancellationToken);

        var mapper = MarkdownPathMapper.Create(
            new AgentPathResolver(Path.Combine(_root, "workspace"), draft))!;
        var handler = new MarkdownPathImageLoaderHandler(mapper);
        string combined = Path.GetFullPath(Path.Combine(mapper.ImageBasePath, "$DRAFT/a.png"));

        await using Stream stream = await handler.LoadAsync(new Uri(combined), CancellationToken.None);
        Assert.Equal(3, stream.Length);
    }
}
