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

        Assert.Contains("can't be viewed", result);
        Assert.DoesNotContain("`", result);
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

        Assert.Equal(mentioned, read.Description.Contains("image", StringComparison.OrdinalIgnoreCase));
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
