using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 探针：票据落盘的三个约束。
///
/// 1. <see cref="TextReasoningContent.ProtectedData"/> 随历史 JSONL 落盘（已验证）
/// 2. 产出方身份（provider+model）能否也随消息落盘
/// 3. <see cref="ChatContentNormalizer"/> 会不会把「空文本 + 有密文」的思考当空思考丢掉
/// </summary>
public class ProtectedDataProbeTests
{
    [Fact]
    public void Probe_ProtectedData_SurvivesRoundTrip()
    {
        ChatMessage message = new(ChatRole.Assistant,
        [
            new TextReasoningContent(string.Empty) { ProtectedData = "ENCRYPTED-BLOB-XYZ" },
            new TextContent("答案"),
        ]);

        string json = JsonSerializer.Serialize(message, SessionJsonOptions.Default.GetTypeInfo(typeof(ChatMessage)));
        System.Console.WriteLine("=== 序列化结果 ===");
        System.Console.WriteLine(json);

        ChatMessage? restored = JsonSerializer.Deserialize(json, SessionJsonOptions.Default.GetTypeInfo(typeof(ChatMessage))) as ChatMessage;
        TextReasoningContent? reasoning = restored?.Contents.OfType<TextReasoningContent>().FirstOrDefault();
        System.Console.WriteLine($"=== ProtectedData 还原后: {reasoning?.ProtectedData ?? "(null)"}");

        Assert.Equal("ENCRYPTED-BLOB-XYZ", reasoning?.ProtectedData);
    }

    /// <summary>产出方身份能不能挂在同一条思考内容上</summary>
    [Fact]
    public void Probe_AdditionalProperties_CarriesProducer()
    {
        TextReasoningContent reasoning = new(string.Empty)
        {
            ProtectedData = "BLOB",
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["producer_provider"] = "commandcode",
                ["producer_model"] = "meta/muse-spark-1.3-contributor",
            },
        };
        ChatMessage message = new(ChatRole.Assistant, [reasoning, new TextContent("答案")]);

        string json = JsonSerializer.Serialize(message, SessionJsonOptions.Default.GetTypeInfo(typeof(ChatMessage)));
        System.Console.WriteLine("=== 序列化结果 ===");
        System.Console.WriteLine(json);

        ChatMessage? restored = JsonSerializer.Deserialize(json, SessionJsonOptions.Default.GetTypeInfo(typeof(ChatMessage))) as ChatMessage;
        TextReasoningContent? back = restored?.Contents.OfType<TextReasoningContent>().FirstOrDefault();
        System.Console.WriteLine($"=== 还原后 ProtectedData: {back?.ProtectedData ?? "(null)"}");
        System.Console.WriteLine($"=== 还原后 AdditionalProperties: {back?.AdditionalProperties?.Count.ToString() ?? "(null)"}");
        if (back?.AdditionalProperties != null)
        {
            foreach (var kv in back.AdditionalProperties)
            {
                System.Console.WriteLine($"    {kv.Key} = {kv.Value} ({kv.Value?.GetType().Name})");
            }
        }
    }

    /// <summary>规整器会不会把带密文的空思考丢掉（合并前后都要在）</summary>
    [Fact]
    public void Probe_Normalizer_KeepsProtectedData()
    {
        ChatMessage message = new(ChatRole.Assistant,
        [
            new TextReasoningContent(string.Empty) { ProtectedData = "BLOB" },
            new TextContent("答案"),
        ]);

        System.Console.WriteLine($"=== 规整前内容数: {message.Contents.Count}");
        ChatContentNormalizer.Normalize(message);
        System.Console.WriteLine($"=== 规整后内容数: {message.Contents.Count}");
        foreach (AIContent content in message.Contents)
        {
            System.Console.WriteLine($"    {content.GetType().Name} protectedData={(content as TextReasoningContent)?.ProtectedData ?? "-"}");
        }

        Assert.Contains(message.Contents, x => x is TextReasoningContent { ProtectedData: "BLOB" });
    }

    /// <summary>相邻的两条思考（一条带密文）合并后，密文还在不在</summary>
    [Fact]
    public void Probe_Normalizer_MergeKeepsProtectedData()
    {
        ChatMessage message = new(ChatRole.Assistant,
        [
            new TextReasoningContent("先看看"),
            new TextReasoningContent(string.Empty) { ProtectedData = "BLOB" },
            new TextContent("答案"),
        ]);

        ChatContentNormalizer.Normalize(message);
        System.Console.WriteLine($"=== 规整后内容数: {message.Contents.Count}");
        foreach (AIContent content in message.Contents)
        {
            string text = content switch
            {
                TextReasoningContent reasoning => reasoning.Text,
                TextContent plain => plain.Text,
                _ => "",
            };
            System.Console.WriteLine($"    {content.GetType().Name} text='{text}' protectedData={(content as TextReasoningContent)?.ProtectedData ?? "-"}");
        }

        Assert.Contains(message.Contents, x => x is TextReasoningContent { ProtectedData: "BLOB" });
    }
}
