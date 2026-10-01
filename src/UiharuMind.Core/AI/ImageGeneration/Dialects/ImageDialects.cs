using System.Net;

namespace UiharuMind.Core.AI.ImageGeneration.Dialects;

/// <summary>
/// 接口格式 → 适配器。适配器无状态，全局各一份，共用一个连接池。
/// </summary>
internal static class ImageDialects
{
    /// <summary>
    /// 生图共用的 HttpClient。不能借对话模型那条：<c>OpenAICompatibleHttpHandler</c> 会把每个请求改写到
    /// chat-completions 端点。客户端本身不设超时，由每次请求按模型的 <see cref="ImageModelInfo.TimeoutSeconds"/> 裁决
    /// </summary>
    internal static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static readonly IImageDialect OpenAI = new OpenAIImageDialect(Http);
    private static readonly IImageDialect SenseNova = new SenseNovaImageDialect(Http);
    private static readonly IImageDialect Agnes = new AgnesImageDialect(Http);

    /// <summary>
    /// 取接口格式的适配器
    /// </summary>
    /// <param name="dialect">接口格式</param>
    /// <returns>适配器</returns>
    public static IImageDialect For(EImageDialect dialect)
    {
        return dialect switch
        {
            EImageDialect.SenseNova => SenseNova,
            EImageDialect.Agnes => Agnes,
            _ => OpenAI,
        };
    }
}
