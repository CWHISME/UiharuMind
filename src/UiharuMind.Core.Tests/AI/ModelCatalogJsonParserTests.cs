using System.Text.Json;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 模型目录 JSON 解析：把服务商 /v1/models 的响应（OpenAI 系 {object,data:[...]} 形状）
/// 解析成候选条目。样例取自 CommandCode 真实响应片段（2026-10-26 抓取）。
/// </summary>
public class ModelCatalogJsonParserTests
{
    private const string SampleJson = """
        {
          "object": "list",
          "data": [
            { "id": "claude-sonnet-5-5", "object": "model", "created": 1792990483, "owned_by": "command-code", "name": "Claude Sonnet 5.5", "context_length": 1000000, "supported_endpoints": ["/messages"] },
            { "id": "gpt-6-astra", "object": "model", "created": 1792990483, "owned_by": "command-code", "name": "GPT-6 Astra", "context_length": 1050000, "supported_endpoints": ["/chat/completions", "/responses"] },
            { "id": "deepseek/deepseek-v4-flash", "object": "model", "created": 1792990483, "owned_by": "command-code", "name": "DeepSeek V4 Flash (latest)", "context_length": 1000000, "supported_endpoints": ["/chat/completions"] }
          ]
        }
        """;

    [Fact]
    public void Parse_RealResponseShape_MapsFields()
    {
        IReadOnlyList<ModelCatalogEntry> entries = ModelCatalogJsonParser.Parse(SampleJson);

        Assert.Equal(3, entries.Count);
        ModelCatalogEntry gpt = entries[1];
        Assert.Equal("gpt-6-astra", gpt.Id);
        Assert.Equal("GPT-6 Astra", gpt.Name);
        Assert.Equal(1050000, gpt.ContextLength);
        Assert.Equal(["/chat/completions", "/responses"], gpt.SupportedEndpoints);
    }

    [Fact]
    public void Parse_MissingOptionalFields_Tolerates()
    {
        const string json = """{ "object": "list", "data": [ { "id": "x", "context_length": 0, "supported_endpoints": [] } ] }""";

        IReadOnlyList<ModelCatalogEntry> entries = ModelCatalogJsonParser.Parse(json);

        ModelCatalogEntry entry = Assert.Single(entries);
        Assert.Equal("x", entry.Id);
        Assert.Equal("", entry.Name);
        Assert.Empty(entry.SupportedEndpoints);
    }

    [Fact]
    public void Parse_EmptyData_ReturnsEmptyList()
    {
        IReadOnlyList<ModelCatalogEntry> entries = ModelCatalogJsonParser.Parse("""{ "object": "list", "data": [] }""");

        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        Assert.Throws<JsonException>(() => ModelCatalogJsonParser.Parse("not json"));
    }

    [Fact]
    public void SupportsEndpoint_MatchesSubstringIgnoreCase()
    {
        var entry = new ModelCatalogEntry { SupportedEndpoints = ["/chat/completions"] };

        Assert.True(entry.SupportsEndpoint("/chat/completions"));
        Assert.False(entry.SupportsEndpoint("/messages"));
    }
}
