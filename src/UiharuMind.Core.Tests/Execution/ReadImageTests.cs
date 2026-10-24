using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.AI.Execution.Tools.Skills;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// Read 看图（ADR 0053）：视觉模型读图片时缩一次存预览副本，结果只给路径，图由发送时投影带给模型；
/// 看不了图的装配如实说，并且只指向真的挂着的工具
/// </summary>
public sealed class ReadImageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-readimage-" + Guid.NewGuid().ToString("N"));

    private string Workspace => Path.Combine(_root, "workspace");

    private string Draft => Path.Combine(_root, "draft");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private AgentPathResolver Paths() => new(Workspace, Draft);

    private Task<string> Read(string path, EReadImageMode mode, ImagePreview.ImageDownscaler? downscaler = null) =>
        new FileReader(Paths(), mode, downscaler).ReadAsync(path, 1, null, TestContext.Current.CancellationToken);

    private async Task WriteFile(string name, byte[] bytes)
    {
        Directory.CreateDirectory(Workspace);
        await File.WriteAllBytesAsync(Path.Combine(Workspace, name), bytes, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Attach_SavesADownscaledPreviewInTheDraftRoom_AndReturnsOnlyPaths()
    {
        byte[] shrunk = TestImages.Jpeg(2, 2);
        await WriteFile("cat.png", TestImages.Png(64, 64));

        string result = await Read("cat.png", EReadImageMode.Attach, (_, _) => (shrunk, "image/jpeg"));

        string preview = Assert.Single(ImagePreview.ParsePreviewPaths(result));
        Assert.True(Paths().TryResolve(preview, out string full));
        Assert.StartsWith(Draft, full);
        Assert.EndsWith(".jpg", full);
        Assert.Equal(shrunk, await File.ReadAllBytesAsync(full, TestContext.Current.CancellationToken));
        Assert.Contains("cat.png", result);
        Assert.Contains("Preview size: 2x2", result);
    }

    [Fact]
    public async Task WithoutADownscaler_TheOriginalIsCopiedAsThePreview()
    {
        byte[] original = TestImages.Png(4, 4);
        await WriteFile("cat.png", original);

        string result = await Read("cat.png", EReadImageMode.Attach);

        Assert.True(Paths().TryResolve(Assert.Single(ImagePreview.ParsePreviewPaths(result)), out string full));
        Assert.Equal(original, await File.ReadAllBytesAsync(full, TestContext.Current.CancellationToken));
    }

    /// <summary>图片扩展名只走看图：内容不是图片就报错，永远不当文本读出来（投影只看扩展名，两边口径要一致）</summary>
    [Theory]
    [InlineData(EReadImageMode.Attach)]
    [InlineData(EReadImageMode.PointToAnalyzeImage)]
    [InlineData(EReadImageMode.None)]
    public async Task ImageExtensionWithOtherContent_IsAnError_NeverText(EReadImageMode mode)
    {
        await WriteFile("notes.png", "Preview: secret.png"u8.ToArray());

        string result = await Read("notes.png", mode);

        Assert.StartsWith("Error:", result);
        Assert.DoesNotContain("secret.png", result);
        Assert.False(Directory.Exists(Path.Combine(Draft, ImagePreview.PreviewFolder)));
    }

    [Fact]
    public async Task ModelThatCannotSee_IsPointedToAnalyzeImage_WithoutWritingAPreview()
    {
        await WriteFile("cat.png", TestImages.Png(40, 30));

        string result = await Read("cat.png", EReadImageMode.PointToAnalyzeImage);

        Assert.Contains("40×30", result);
        Assert.Contains($"`{VisionTool.ToolName}`", result);
        Assert.Empty(ImagePreview.ParsePreviewPaths(result));
        Assert.False(Directory.Exists(Path.Combine(Draft, ImagePreview.PreviewFolder)));
    }

    /// <summary>识图开关关着：哪个工具都没挂，就不能指名任何一个</summary>
    [Fact]
    public async Task NoVision_SaysImagesCannotBeViewed_WithoutNamingATool()
    {
        await WriteFile("cat.png", TestImages.Png(4, 4));

        string result = await Read("cat.png", EReadImageMode.None);

        Assert.Contains("无法查看图片", result);
        Assert.DoesNotContain("`", result);
    }

    // —— 多图拼接（分号）：完整路径不存在才拆，存在优先；批量与 AnalyzeImage 同上限、同「整体报错」口径 ——

    [Fact]
    public async Task SemicolonJoin_AttachesEveryImage_ReturningOnePreviewPerImage()
    {
        await WriteFile("a.png", TestImages.Png(8, 8));
        await WriteFile("b.png", TestImages.Png(16, 16));

        string result = await Read("a.png;b.png", EReadImageMode.Attach);

        IReadOnlyList<string> previews = ImagePreview.ParsePreviewPaths(result);
        Assert.Equal(2, previews.Count);
        foreach (string preview in previews)
        {
            Assert.True(Paths().TryResolve(preview, out string full));
            Assert.StartsWith(Draft, full);
        }

        Assert.Contains("a.png", result);
        Assert.Contains("b.png", result);
    }

    [Fact]
    public async Task SemicolonJoin_WithNonImageEntry_IsAnError_NothingAttached()
    {
        await WriteFile("a.png", TestImages.Png(8, 8));
        await WriteFile("notes.txt", "hello"u8.ToArray());

        string result = await Read("a.png;notes.txt", EReadImageMode.Attach);

        Assert.StartsWith("Error:", result);
        Assert.Contains("notes.txt", result);
        Assert.False(Directory.Exists(Path.Combine(Draft, ImagePreview.PreviewFolder)));
    }

    [Fact]
    public async Task SemicolonJoin_WithMissingImage_IsAnError_ListingMissing()
    {
        await WriteFile("a.png", TestImages.Png(8, 8));

        string result = await Read("a.png;missing.png", EReadImageMode.Attach);

        Assert.StartsWith("Error:", result);
        Assert.Contains("missing.png", result);
        Assert.False(Directory.Exists(Path.Combine(Draft, ImagePreview.PreviewFolder)));
    }

    [Fact]
    public async Task SemicolonJoin_MoreThanEight_IsAnError()
    {
        List<string> names = new();
        for (int i = 0; i < VisionTool.MaxImages + 1; i++)
        {
            string name = $"a{i}.png";
            names.Add(name);
            await WriteFile(name, TestImages.Png(2, 2));
        }

        string result = await Read(string.Join(";", names), EReadImageMode.Attach);

        Assert.StartsWith("Error:", result);
        Assert.Contains(VisionTool.MaxImages.ToString(), result);
    }

    [Fact]
    public async Task SemicolonInRealFilename_ExistingFileIsReadWhole_NotSplit()
    {
        // 文件名里带分号的真实文件：存在优先，不拆
        await WriteFile("a;b.png", TestImages.Png(8, 8));

        string result = await Read("a;b.png", EReadImageMode.Attach);

        Assert.Single(ImagePreview.ParsePreviewPaths(result));
        Assert.Contains("a;b.png", result);
    }

    [Fact]
    public async Task SemicolonInRealTextFilename_IsReadAsText_NotSplit()
    {
        await WriteFile("a;b.txt", "hello world"u8.ToArray());

        string result = await Read("a;b.txt", EReadImageMode.Attach);

        Assert.False(result.StartsWith("Error:", StringComparison.Ordinal));
        Assert.Contains("hello world", result);
    }

    [Fact]
    public void IsImageRead_RecognizesSemicolonJoinedPaths()
    {
        Assert.True(ImagePreview.IsImageRead(
            new FunctionCallContent("c1", FileToolNames.Read, new Dictionary<string, object?> { ["filePath"] = "a.png;b.png" })));
        Assert.True(ImagePreview.IsImageRead(
            new FunctionCallContent("c2", FileToolNames.Read, new Dictionary<string, object?> { ["filePath"] = "cat.png" })));
        Assert.False(ImagePreview.IsImageRead(
            new FunctionCallContent("c3", FileToolNames.Read, new Dictionary<string, object?> { ["filePath"] = "a;b.txt" })));
    }

    /// <summary>描述只在会附图时提图片，别让看不了图的模型以为能看</summary>
    [Theory]
    [InlineData(EReadImageMode.Attach, true)]
    [InlineData(EReadImageMode.PointToAnalyzeImage, false)]
    [InlineData(EReadImageMode.None, false)]
    public void ReadDescription_MentionsImagesOnlyWhenItAttachesThem(EReadImageMode mode, bool mentioned)
    {
        AIFunction read = new PermissiveFileAccessTools(Paths(), imageMode: mode).Create()
            .OfType<AIFunction>().Single(tool => tool.Name == FileToolNames.Read);

        Assert.Equal(mentioned, read.Description.Contains("图片", StringComparison.Ordinal));
    }

    /// <summary>与 AnalyzeImage 共用识图开关、按模型互斥：自带视觉的经 Read 自己看，看不了的转交</summary>
    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, false)] //识图开关关了:两边都不成立
    public void Mount_SharesTheVisionToggle_AndPicksByModel(bool enabled, bool modelSeesImages, bool attach, bool analyze)
    {
        CharacterData character = new()
        {
            CharacterId = "a", IsAgent = true, Tools = new AgentToolConfig { EnableVisionTool = enabled },
        };
        AgentAssemblyPlan plan = new()
        {
            Profile = new AgentBuildProfile { Character = character }, ModelSupportsVision = modelSeesImages,
        };

        Assert.Equal(attach, plan.ReadAttachesImages);
        Assert.Equal(analyze, plan.MountVisionTool);
    }

    /// <summary>
    /// 投影跟着识图开关走：关掉就不再重发看过的图（模型只剩结果里的路径文本），
    /// 用户才有办法停掉这笔每次请求都要付的钱
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Projection_FollowsTheVisionToggle(bool enabled, bool projected)
    {
        CharacterData character = new()
        {
            CharacterId = "a",
            IsAgent = true,
            Tools = new AgentToolConfig { EnableShellExecution = false, EnableVisionTool = enabled },
        };
        AgentAssemblyPlan plan = new()
        {
            Profile = new AgentBuildProfile { Character = character, PermissionMode = EAgentPermissionMode.AutoEdit },
            WorkingDirectory = Workspace,
            SkillsSource = new AgentFileSkillsSource(Path.Combine(_root, "skills")),
            Mcp = McpToolSet.Empty,
            ModelSupportsVision = true,
        };

        await using AgentHandle handle = AgentAssembler.Assemble(plan);

        Assert.Equal(projected, handle.ServiceCalls.GetService<ImagePreviewProjectingChatClient>() != null);
    }
}
