using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// AnalyzeImage 工具的输入面:imagePaths 是数组、缺失整体报错并列出全部缺失路径、
/// 单次上限 MaxImages。视觉模型那半边需要真实模型,这里只验工具侧的分支。
/// </summary>
public class VisionToolTests
{
    private static AIFunction Tool()
    {
        return (AIFunction)VisionTool.Create(Path.Combine(Path.GetTempPath(), "uiharu-vision-test"));
    }

    private static async Task<string> Invoke(string[] paths, string question)
    {
        object? raw = await Tool().InvokeAsync(new AIFunctionArguments
        {
            ["imagePaths"] = paths,
            ["question"] = question,
        }, TestContext.Current.CancellationToken);
        return raw is JsonElement { ValueKind: JsonValueKind.String } element
            ? element.GetString() ?? string.Empty
            : raw?.ToString() ?? string.Empty;
    }

    [Fact]
    public async Task MissingFiles_ReportAllPaths()
    {
        string result = await Invoke(["/definitely/not/a.png", "/definitely/not/b.png"], "compare");

        Assert.Contains("not found", result);
        Assert.Contains("a.png", result);
        Assert.Contains("b.png", result);
    }

    [Fact]
    public async Task MoreThanMaxImages_Rejected()
    {
        string[] paths = Enumerable.Range(0, VisionTool.MaxImages + 1)
            .Select(i => $"/tmp/img{i}.png").ToArray();

        string result = await Invoke(paths, "compare");

        Assert.Contains("at most", result);
        Assert.Contains(VisionTool.MaxImages.ToString(), result);
    }

    [Fact]
    public async Task EmptyPaths_Rejected()
    {
        string result = await Invoke([], "what");

        Assert.Contains("at least one", result);
    }
}
