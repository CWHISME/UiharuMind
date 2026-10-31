using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.ImageGeneration;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// ListImageModels 的工具侧：名字、顺序、各自此刻的状态，以及没配模型时指条路。
/// 出图本身见 ImageGenerationToolTests
/// </summary>
public sealed class ImageModelsToolTests
{
    private static ImageGenerationService Service(params ImageModelInfo[] models) => new(() => models,
        _ => throw new InvalidOperationException("查清单不该出图"),
        (_, _) => throw new InvalidOperationException("查清单不该下载"));

    [Fact]
    public void Describe_ListsModelsInFallbackOrder_WithState()
    {
        string text = ImageModelsTool.Describe(Service(
            new ImageModelInfo { Name = "painter", Endpoint = "https://example.com/v1", ModelId = "gpt-image-1" },
            new ImageModelInfo { Name = "unfilled", SupportsEditing = false }));

        Assert.Contains("生图模型 2 个", text);
        // 一行一家，名字在前：回退顺序就是列表顺序，模型照着挑
        Assert.Contains("- painter · OpenAI 兼容 · 2K · 可编辑 · 就绪", text);
        Assert.Contains("- unfilled · OpenAI 兼容 · 2K · 只能生成 · 未配置", text);
        Assert.True(text.IndexOf("- painter", StringComparison.Ordinal) <
                    text.IndexOf("- unfilled", StringComparison.Ordinal));
        // 点名与不点名的差别说清楚：不点就回退，点了只试那几家
        Assert.Contains("不再自动换家", text);
    }

    [Fact]
    public void Describe_WithNoModel_SaysWhereToAddOne()
    {
        Assert.Contains("设置 → 图像生成", ImageModelsTool.Describe(Service()));
    }

    [Fact]
    public async Task Tool_RunsUnderItsOwnName()
    {
        AIFunction tool = (AIFunction)ImageModelsTool.Create(
            Service(new ImageModelInfo { Name = "painter", Endpoint = "https://example.com/v1", ModelId = "x" }));

        object? raw = await tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        Assert.Equal("ListImageModels", tool.Name);
        string text = raw is JsonElement { ValueKind: JsonValueKind.String } element
            ? element.GetString() ?? string.Empty
            : raw?.ToString() ?? string.Empty;
        Assert.Contains("painter", text);
    }
}
