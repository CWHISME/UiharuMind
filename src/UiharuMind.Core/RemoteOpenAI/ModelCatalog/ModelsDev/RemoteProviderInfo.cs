using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>
/// models.dev 里一家供应商的接入信息。<b>只收能直接用 HTTP 打的那几家</b>——
/// 没有 <c>api</c> 字段的（mistral / xai / google / azure / bedrock …）走各家原生 SDK，
/// 地址里带 <c>${VAR}</c> 占位的（databricks / cloudflare …）填进表单也是废的，两者都在建快照时滤掉。
/// </summary>
/// <param name="Id">models.dev 的 provider 键（<c>groq</c>、<c>moonshotai</c>…），落库时写进 <c>CatalogProviderKey</c></param>
/// <param name="Name">显示名</param>
/// <param name="ApiBaseUrl">OpenAI 兼容基址（多数以 <c>/v1</c> 结尾）</param>
/// <param name="DocUrl">官方文档/控制台地址，界面上「打开官网」用</param>
/// <param name="Npm">models.dev 声明的 SDK 包名，用来判该走哪条线</param>
/// <param name="ModelCount">该家收录的模型数，选择器里显示，用来判断值不值得配</param>
public sealed record RemoteProviderInfo(
    string Id,
    string Name,
    string ApiBaseUrl,
    string DocUrl,
    string Npm,
    int ModelCount)
{
    /// <summary>
    /// 该家默认走哪条线。地址尾段认不出格式时（裸 <c>/v1</c>）只能靠这个给初值：
    /// anthropic 系走 Messages，其余（openai-compatible / openai / 各家转售商）走 ChatCompletions。
    /// </summary>
    public EApiFormat ApiFormat => Npm.Contains("anthropic", StringComparison.OrdinalIgnoreCase)
        ? EApiFormat.Messages
        : EApiFormat.ChatCompletions;

    /// <summary>模型目录地址（<c>{api}/models</c>），与目录拉取器同一个口径</summary>
    public string CatalogUrl => ApiBaseUrl.TrimEnd('/') + "/models";
}
