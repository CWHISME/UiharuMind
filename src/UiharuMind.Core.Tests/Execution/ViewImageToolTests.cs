using System.Text.Json;
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
/// ViewImage（ADR 0053）：调用时缩一次存预览副本，结果只给路径；图由发送时投影带给模型
/// </summary>
public sealed class ViewImageToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-viewimage-" + Guid.NewGuid().ToString("N"));

    private string Workspace => Path.Combine(_root, "workspace");

    private string Draft => Path.Combine(_root, "draft");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private AgentPathResolver Paths() => new(Workspace, Draft);

    private async Task<string> Invoke(string[] images, ViewImageTool.ImageDownscaler? downscaler = null)
    {
        AIFunction tool = (AIFunction)ViewImageTool.Create(Paths(), downscaler);
        object? raw = await tool.InvokeAsync(new AIFunctionArguments { ["paths"] = images },
            TestContext.Current.CancellationToken);
        return raw is JsonElement { ValueKind: JsonValueKind.String } element
            ? element.GetString() ?? string.Empty
            : raw?.ToString() ?? string.Empty;
    }

    private async Task WriteImage(string name, byte[] bytes)
    {
        Directory.CreateDirectory(Workspace);
        await File.WriteAllBytesAsync(Path.Combine(Workspace, name), bytes, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Viewing_SavesADownscaledPreviewInTheDraftRoom_AndReturnsOnlyPaths()
    {
        byte[] shrunk = TestImages.Jpeg(2, 2);
        await WriteImage("cat.png", TestImages.Png(64, 64));

        string result = await Invoke(["cat.png"], (_, _) => (shrunk, "image/jpeg"));

        string preview = Assert.Single(ViewImageTool.ParsePreviewPaths(result));
        Assert.True(Paths().TryResolve(preview, out string full));
        Assert.StartsWith(Draft, full);
        Assert.EndsWith(".jpg", full);
        Assert.Equal(shrunk, await File.ReadAllBytesAsync(full, TestContext.Current.CancellationToken));
        Assert.Contains("cat.png", result);
    }

    /// <summary>任一张不行就整体报错，不搞「缺的跳过、剩的照发」——与 AnalyzeImage 同口径</summary>
    [Fact]
    public async Task MissingOrNonImageFiles_FailTheWholeCall_WithoutWritingPreviews()
    {
        await WriteImage("ok.png", TestImages.Png(4, 4));
        await WriteImage("notes.png", "not an image"u8.ToArray());

        string missing = await Invoke(["ok.png", "gone.png"]);
        string notImage = await Invoke(["ok.png", "notes.png"]);

        Assert.StartsWith("Error:", missing);
        Assert.Contains("gone.png", missing);
        Assert.StartsWith("Error:", notImage);
        Assert.Contains("notes.png", notImage);
        Assert.False(Directory.Exists(Path.Combine(Draft, ViewImageTool.PreviewFolder)));
    }

    [Fact]
    public async Task WithoutADownscaler_TheOriginalIsCopiedAsThePreview()
    {
        byte[] original = TestImages.Png(4, 4);
        await WriteImage("cat.png", original);

        string result = await Invoke(["cat.png"]);

        Assert.True(Paths().TryResolve(Assert.Single(ViewImageTool.ParsePreviewPaths(result)), out string full));
        Assert.Equal(original, await File.ReadAllBytesAsync(full, TestContext.Current.CancellationToken));
    }

    /// <summary>与 AnalyzeImage 共用识图开关、按模型互斥：自带视觉的自己看，看不了的转交</summary>
    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, false)] //识图开关关了:两个都不挂
    public void Mount_SharesTheVisionToggle_AndPicksByModel(bool enabled, bool modelSeesImages, bool view, bool analyze)
    {
        CharacterData character = new()
        {
            CharacterId = "a", IsAgent = true, Tools = new AgentToolConfig { EnableVisionTool = enabled },
        };
        AgentAssemblyPlan plan = new()
        {
            Profile = new AgentBuildProfile { Character = character }, ModelSupportsVision = modelSeesImages,
        };

        Assert.Equal(view, plan.MountViewImage);
        Assert.Equal(analyze, plan.MountVisionTool);
    }

    /// <summary>
    /// 投影跟着看图工具走：关掉识图开关就不再重发看过的图（模型只剩结果里的路径文本），
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

        Assert.Equal(projected, handle.ServiceCalls.GetService<ViewImageProjectingChatClient>() != null);
    }
}
