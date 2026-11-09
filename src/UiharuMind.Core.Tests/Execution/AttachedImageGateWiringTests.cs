using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 装配闸门：判据只在「视觉模型 + 识图开关开着（于是 Read 附图）」且装配交了
/// <see cref="AgentToolResources.SeesImagesNow"/> 时才接进 Read。
/// 判据本身由 <see cref="TurnAttachedImagesTests"/> 钉住；这里钉的是**接线**——
/// 关掉识图、或没交那个来源（子代理就是这样）时，Read 必须回到从前的行为。
/// </summary>
public sealed class AttachedImageGateWiringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-gate-" + Guid.NewGuid().ToString("N"));

    private string Workspace => Path.Combine(_root, "workspace");

    private string Draft => Path.Combine(_root, "draft");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task WithAVisionModel_TheReadToolAnswersFromTheMessageInFront()
    {
        byte[] image = TestImages.Png(8, 8);
        ChatSession session = SessionShowing(image);
        await WriteFile("cat.png", image);

        string result = await ReadThrough(resources => resources with
        {
            ModelSupportsVision = true, SeesImagesNow = () => true, Session = () => session,
        });

        Assert.Contains("already attached", result);
        Assert.Empty(ImagePreview.ParsePreviewPaths(result));
    }

    /// <summary>没有那个来源（子代理装配就是这样）：不判，照旧烘预览</summary>
    [Fact]
    public async Task WithoutTheSource_TheReadToolStillAttaches()
    {
        byte[] image = TestImages.Png(8, 8);
        ChatSession session = SessionShowing(image);
        await WriteFile("cat.png", image);

        string result = await ReadThrough(resources => resources with
        {
            ModelSupportsVision = true, SeesImagesNow = null, Session = () => session,
        });

        Assert.Single(ImagePreview.ParsePreviewPaths(result));
        Assert.Contains("The image follows", result);
    }

    /// <summary>看不了图的装配（Read 走 AnalyzeImage 那条路）：判据压根不接</summary>
    [Fact]
    public async Task WithoutVision_TheReadToolPointsToAnalyzeImage()
    {
        byte[] image = TestImages.Png(8, 8);
        ChatSession session = SessionShowing(image);
        await WriteFile("cat.png", image);

        string result = await ReadThrough(resources => resources with
        {
            ModelSupportsVision = false, SeesImagesNow = () => true, Session = () => session,
        });

        Assert.Contains(VisionTool.ToolName, result);
        Assert.DoesNotContain("already attached", result);
    }

    /// <summary>
    /// 子代理走的是同一条 <see cref="AgentToolsetBuilder"/>，资源却是另一处造的：
    /// 那里也得把「这一发看不看得见图」交下去，否则子会话里 Read 已附图时不会回提示
    /// （子会话是可打开的会话，用户能给它附图，ADR 0021）
    /// </summary>
    [Fact]
    public async Task SubAgentAssembly_WiresTheSameGate_FromItsOwnSession()
    {
        byte[] image = TestImages.Png(8, 8);
        ChatSession session = SessionShowing(image);
        SessionManager.Instance.Add(session);
        try
        {
            await WriteFile("cat.png", image);
            HarnessAgentOptions? options = SubAgentAssembly.BuildSubAgentOptions(new SubAgentAssembly.SubAgentAssemblyInput
            {
                Config = new AgentToolConfig { EnableFileAccess = true, EnableVisionTool = true },
                WorkingDirectory = Workspace,
                ModelSupportsVision = true,
                SeesImagesNow = () => true,
                SessionId = session.SessionId,
                OutputFolderName = "gate-wiring",
            });

            Assert.NotNull(options);
            AIFunction read = options.ChatOptions!.Tools!.OfType<AIFunction>().Single(x => x.Name == FileToolNames.Read);
            object? result = await read.InvokeAsync(
                new AIFunctionArguments(new Dictionary<string, object?> { ["filePath"] = "cat.png" }),
                TestContext.Current.CancellationToken);

            Assert.Contains("already attached", Assert.IsType<string>(result));
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    // 走真装配那条路：把资源交给 AgentToolsetBuilder，再从它产出的工具里点 Read
    private async Task<string> ReadThrough(Func<AgentToolResources, AgentToolResources> customize)
    {
        AgentToolResources resources = customize(new AgentToolResources
        {
            Paths = new AgentPathResolver(Workspace, Draft), ModelSupportsVision = true, SeesImagesNow = () => true,
        });
        List<AgentToolEntry> entries = AgentToolsetBuilder.Build(
            new AgentToolConfig { EnableFileAccess = true, EnableVisionTool = true }, EAgentToolScope.Main, resources);
        AIFunction read = entries.Select(x => x.Tool).OfType<AIFunction>().Single(x => x.Name == FileToolNames.Read);

        object? result = await read.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["filePath"] = "cat.png" }),
            TestContext.Current.CancellationToken);
        return Assert.IsType<string>(result);
    }

    // 会话里最后一条用户消息就带着这张图（组装时内联的那一份；测试没装缩图器，内联字节即原文）
    private static ChatSession SessionShowing(byte[] image)
    {
        ChatSession session = new("test", new CharacterData { CharacterId = "test" }) { IsTransient = true };
        session.History.Add(new ChatMessage(ChatRole.User,
        [
            new DataContent(image, "image/png"),
            new TextContent("看这张\n\n***\n[Attached file: cat.png]"),
        ]));
        return session;
    }

    private async Task WriteFile(string name, byte[] bytes)
    {
        Directory.CreateDirectory(Workspace);
        await File.WriteAllBytesAsync(Path.Combine(Workspace, name), bytes, TestContext.Current.CancellationToken);
    }
}
