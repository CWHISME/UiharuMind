using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 截图得看得了才提供：视觉模型要有 Read 才看得见，非视觉模型要有 AnalyzeImage；都没有就不在工具说明里出现
/// </summary>
public class BrowserAssemblyTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, false)]
    public async Task Screenshots_AreOfferedOnlyWhenTheImageCanBeSeen(bool modelSeesImages, bool visionTool, bool files,
        bool offered)
    {
        AgentToolConfig config = new() { EnableVisionTool = visionTool, EnableFileAccess = files };
        AgentToolResources resources = new()
        {
            Paths = new AgentPathResolver("/tmp/uiharu-ws", "/tmp/uiharu-ws-room", string.Empty),
            SessionId = "s1",
            OutputRoomDirectory = "/tmp/uiharu-ws-room",
            ModelSupportsVision = modelSeesImages,
            Browser = PuppeteerBrowserHost.Shared,
        };

        // 浏览器是扩展（ADR 0079）：观察命令的说明经帮助查到
        AIFunction help = AgentToolsetBuilder.Build(config, EAgentToolScope.Main, resources)
            .Select(entry => entry.Tool).OfType<AIFunction>().Single(t => t.Name == ExtensionCalls.HelpName);
        string snapshot = Assert.IsType<string>(await help.InvokeAsync(new AIFunctionArguments
        {
            ["extension"] = BuiltinExtensions.Browser, ["command"] = BuiltinExtensions.BrowserSnapshot,
        }, TestContext.Current.CancellationToken));

        Assert.Equal(offered, snapshot.Contains("screenshot", StringComparison.Ordinal));
    }
}
