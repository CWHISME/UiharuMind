namespace UiharuMind.Core.AI.Net.Transport;

/// <summary>
/// 用户填的地址 → 真正要打的 <c>chat/completions</c> 地址。
///
/// 远程配置既允许填完整接口，也允许只填 OpenAI 兼容服务的根路径（<c>https://x/v1</c> 或 <c>https://x</c>）；
/// SDK 自己会拼标准路径，所以这一步必须把三条形态收敛成同一个结果，否则请求会打到 <c>/v1/v1/chat/completions</c>。
/// </summary>
internal static class ChatCompletionUri
{
    /// <summary>
    /// 把配置里的地址补成完整的 chat completions 端点
    /// </summary>
    /// <param name="address">用户配置的地址</param>
    /// <returns>补全路径后的地址构造器</returns>
    public static UriBuilder Resolve(string address)
    {
        var builder = new UriBuilder(address);
        string path = builder.Path.TrimEnd('/');
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            builder.Path = path + "/chat/completions";
        else if (string.IsNullOrEmpty(path))
            builder.Path = "/v1/chat/completions";
        return builder;
    }

    /// <summary>
    /// 这个端点是不是聊天补全（只有它的响应需要清洗；其它路径的响应交给调用方原样处理）
    /// </summary>
    /// <param name="uri">已补全的端点地址</param>
    /// <returns>是聊天补全返回 true</returns>
    public static bool IsChatCompletions(Uri uri) =>
        uri.AbsolutePath.Contains("chat/completions", StringComparison.OrdinalIgnoreCase);
}
