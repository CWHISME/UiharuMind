namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>
/// models.dev 不给地址的那几家的默认基址。
///
/// <para>models.dev 只在「地址由各家自己配」时才写 <c>api</c> 字段；一旦地址被编进 SDK 包
/// （<c>@ai-sdk/openai</c>、<c>@ai-sdk/groq</c>…），这个字段就空着。而这几家恰好是用户最常配的
/// ——按「有 api 才收」一刀切，OpenAI / Anthropic / Google / Groq / xAI 全都会从列表里消失，
/// 功能看起来就是坏的。</para>
///
/// <para>所以这里留一张<b>按 SDK 包名</b>（不是按供应商）的小表：同包的新供应商也自动受益。
/// 只收地址稳定、且确实是 OpenAI 兼容口的；Azure / Bedrock / Vertex 这种地址里带账户的
/// 一律不收——猜一个错的地址比不给更糟。这张表是<b>唯一</b>手写的端点数据，加条目要能说出官方出处。</para>
/// </summary>
internal static class SdkEndpointFallbacks
{
    private static readonly Dictionary<string, string> ByNpm = new(StringComparer.OrdinalIgnoreCase)
    {
        ["@ai-sdk/openai"] = "https://api.openai.com/v1",
        ["@ai-sdk/anthropic"] = "https://api.anthropic.com/v1",
        // Gemini 的 OpenAI 兼容口（原生 generateContent 不是 OpenAI 形状）
        ["@ai-sdk/google"] = "https://generativelanguage.googleapis.com/v1beta/openai",
        ["@ai-sdk/mistral"] = "https://api.mistral.ai/v1",
        ["@ai-sdk/xai"] = "https://api.x.ai/v1",
        ["@ai-sdk/groq"] = "https://api.groq.com/openai/v1",
        ["@ai-sdk/cerebras"] = "https://api.cerebras.ai/v1",
        ["@ai-sdk/deepinfra"] = "https://api.deepinfra.com/v1/openai",
        ["@ai-sdk/togetherai"] = "https://api.together.xyz/v1",
        ["@ai-sdk/perplexity"] = "https://api.perplexity.ai",
        ["@ai-sdk/cohere"] = "https://api.cohere.ai/compatibility/v1",
    };

    /// <summary>按 SDK 包名取默认基址；不认识返回空串</summary>
    /// <param name="npm">models.dev 的 <c>npm</c> 字段</param>
    public static string Resolve(string? npm) =>
        npm is { Length: > 0 } && ByNpm.TryGetValue(npm, out string? api) ? api : "";
}
