namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 新建生图模型时的服务商预设。只在建时预填一次，之后各存各的。
/// 预设是数据而不是子类：同一家的差别只有这几个字段，行为全在接口格式里。
/// </summary>
/// <param name="Key">稳定标识</param>
/// <param name="DisplayName">下拉框里的名字（品牌名不翻译；自定义由界面按 <see cref="CustomKey"/> 本地化）</param>
/// <param name="Dialect">接口格式</param>
/// <param name="Endpoint">服务根地址；自定义为空</param>
/// <param name="ModelIds">可选的模型 id，首个为默认</param>
/// <param name="SupportsEditing">是否支持编辑</param>
/// <param name="WebsiteUrl">开放平台地址，界面一键跳转用；空表示没有</param>
/// <param name="ExtraBody">默认私有参数</param>
public sealed record ImageModelPreset(
    string Key,
    string DisplayName,
    EImageDialect Dialect,
    string Endpoint,
    IReadOnlyList<string> ModelIds,
    bool SupportsEditing,
    string WebsiteUrl = "",
    string ExtraBody = "")
{
    /// <summary>自定义：只给格式，地址与模型自己填</summary>
    public const string CustomKey = "custom";

    /// <summary>全部预设，自定义在最后</summary>
    public static IReadOnlyList<ImageModelPreset> All { get; } =
    [
        // 水印显式写出：官方建议别依赖默认值，去水印公测后会转付费
        new("sensenova", "SenseNova", EImageDialect.SenseNova, "https://token.sensenova.cn/v1",
            ["sensenova-u1.5-lite", "sensenova-u1.5-fast"], true, "https://platform.sensenova.cn",
            "{\"watermark\": true}"),
        new("agnes", "Agnes AI", EImageDialect.Agnes, "https://api.agnes-ai.cn/v1",
            ["agnes-image-2.5-flash"], true, "https://wiki.agnes-ai.cn"),
        new("openai", "OpenAI", EImageDialect.OpenAI, "https://api.openai.com/v1",
            ["gpt-image-1"], true, "https://platform.openai.com"),
        new(CustomKey, "Custom", EImageDialect.OpenAI, "", [], true),
    ];
}
