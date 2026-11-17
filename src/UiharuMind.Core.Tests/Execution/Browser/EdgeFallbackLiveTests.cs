using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 真机验证 Edge 回退（本机没有 Chrome、只有 Edge 时正好覆盖回退路径），显式跑：
/// ./src/UiharuMind.Core.Tests/bin/Debug/net10.0/UiharuMind.Core.Tests --filter "FullyQualifiedName~EdgeFallbackLiveTests"
/// </summary>
public class EdgeFallbackLiveTests
{
    [Fact(Explicit = true)]
    public async Task Live_Headless_RendersWithEdgeWhenChromeIsMissing()
    {
        (string _, string html) = await HeadlessPageRenderer.Shared.RenderAsync(
            "data:text/html,<title>edge-fallback</title><p>edge-ok</p>",
            TestContext.Current.CancellationToken);

        Assert.Contains("edge-ok", html);
    }
}