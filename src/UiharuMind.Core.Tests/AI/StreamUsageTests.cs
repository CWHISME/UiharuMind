using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 一次流式调用只记一份用量：有的服务商（实测 SenseNova 上的 deepseek-flash）在收尾帧与末帧各报一次累计用量，
/// 两份都记账就翻倍
/// </summary>
public class StreamUsageTests
{
    private static ChatResponseUpdate Update(params AIContent[] contents) => new(ChatRole.Assistant, contents);

    private static UsageContent Usage(long input) => new(new UsageDetails { InputTokenCount = input });

    private static async IAsyncEnumerable<ChatResponseUpdate> Stream(params ChatResponseUpdate[] updates)
    {
        foreach (ChatResponseUpdate update in updates)
        {
            await Task.Yield();
            yield return update;
        }
    }

    private static async Task<List<ChatResponseUpdate>> Collect(IAsyncEnumerable<ChatResponseUpdate> stream)
    {
        List<ChatResponseUpdate> list = new();
        await foreach (ChatResponseUpdate update in stream) list.Add(update);
        return list;
    }

    [Fact]
    public async Task RepeatedCumulativeUsage_IsReportedOnce_TakingTheLast()
    {
        List<ChatResponseUpdate> output = await Collect(StreamUsage.KeepLast(Stream(
            Update(new TextContent("a")),
            Update(new TextContent("b"), Usage(10)),
            Update(Usage(12)))));

        UsageContent usage = Assert.Single(output.SelectMany(u => u.Contents).OfType<UsageContent>());
        Assert.Equal(12, usage.Details.InputTokenCount);
        Assert.Equal("ab", string.Concat(output.SelectMany(u => u.Contents).OfType<TextContent>().Select(t => t.Text)));
    }

    [Fact]
    public async Task ASingleUsage_StillArrivesOnce_AfterTheText()
    {
        List<ChatResponseUpdate> output = await Collect(StreamUsage.KeepLast(Stream(
            Update(new TextContent("a")),
            Update(new TextContent("b"), Usage(7)))));

        List<AIContent> contents = output.SelectMany(u => u.Contents).ToList();
        Assert.Equal(7, Assert.Single(contents.OfType<UsageContent>()).Details.InputTokenCount);
        Assert.IsType<UsageContent>(contents[^1]);
        Assert.Equal("ab", string.Concat(contents.OfType<TextContent>().Select(t => t.Text)));
    }
}
