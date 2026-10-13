using System.Text.Json;
using System.Text.Json.Serialization;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 采样参数的推荐值
/// </summary>
/// <param name="Steps">步数</param>
/// <param name="Cfg">CFG</param>
public sealed record ImageCatalogSampling(int Steps, float Cfg)
{
    public string? Sampler { get; init; }
    public string? Scheduler { get; init; }
    public float? FlowShift { get; init; }
    public float? DistilledGuidance { get; init; }
}

/// <summary>
/// 档位里的一个组件：哪个仓库的哪个文件，交给 sd-server 的哪个参数
/// </summary>
/// <param name="Flag">启动参数名（不带横线）</param>
/// <param name="Repo">owner/repo</param>
/// <param name="File">仓库内路径</param>
/// <param name="Size">字节数</param>
/// <param name="Sha256">sha256；没有为 null</param>
public sealed record ImageCatalogComponent(string Flag, string Repo, string File, long Size, string? Sha256);

/// <summary>
/// 一个档位：把所有槽位的文件都定死，用户只挑档、不逐个挑量化
/// </summary>
/// <param name="Id">lite / balanced / quality</param>
/// <param name="Components">组件</param>
public sealed record ImageCatalogTier(string Id, IReadOnlyList<ImageCatalogComponent> Components)
{
    /// <summary>
    /// 这个家族默认挑这一档
    /// </summary>
    public bool Recommended { get; init; }

    /// <summary>
    /// 所有组件大小之和
    /// </summary>
    public long TotalSize => Components.Sum(x => x.Size);
}

/// <summary>
/// 一个可下载的生图模型家族（照 sd.cpp 文档转写）
/// </summary>
/// <param name="Id">家族 id</param>
/// <param name="Name">显示名</param>
/// <param name="Description">各语言的简介（en / zh-hans）</param>
/// <param name="Doc">sd.cpp 文档页</param>
/// <param name="Recommended">推荐先装</param>
/// <param name="Tested">实际出过图；没有的标「未测试」</param>
/// <param name="SupportsEditing">给参考图能改图</param>
/// <param name="BaseResolution">基准边长：按比例换算宽高时面积取它的平方</param>
/// <param name="Sampling">推荐采样参数</param>
/// <param name="Tiers">档位</param>
public sealed record ImageCatalogFamily(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Description,
    string Doc,
    bool Recommended,
    bool Tested,
    bool SupportsEditing,
    int BaseResolution,
    ImageCatalogSampling Sampling,
    IReadOnlyList<ImageCatalogTier> Tiers)
{
    /// <summary>
    /// 推荐的负向提示词
    /// </summary>
    public string NegativePrompt { get; init; } = "";

    /// <summary>
    /// 这个家族要的固定启动参数
    /// </summary>
    public IReadOnlyList<string> ExtraArguments { get; init; } = [];

    /// <summary>
    /// 默认挑的那一档
    /// </summary>
    public ImageCatalogTier DefaultTier => Tiers.FirstOrDefault(x => x.Recommended) ?? Tiers[0];

    /// <summary>
    /// 某种语言的简介；没有就英文
    /// </summary>
    /// <param name="language">语言代码，如 zh-hans</param>
    public string DescriptionFor(string language) =>
        Description.TryGetValue(language, out string? text) ? text : Description.GetValueOrDefault("en", "");
}

/// <summary>
/// 应用内置的可下载生图模型（随应用发版，照固定的那版 sd.cpp 文档转写，每个文件都用 HF API 核对过）
/// </summary>
/// <param name="EngineBuild">对得上的 sd.cpp 发布</param>
/// <param name="Families">家族</param>
public sealed record ImageModelCatalog(string EngineBuild, IReadOnlyList<ImageCatalogFamily> Families)
{
    private const string ResourceName = "UiharuMind.Core.AI.ImageGeneration.Local.image-models.json";

    private static readonly Lazy<ImageModelCatalog> BuiltInInstance = new(LoadBuiltIn);

    /// <summary>
    /// 内置的那份
    /// </summary>
    public static ImageModelCatalog BuiltIn => BuiltInInstance.Value;

    /// <summary>
    /// 按 id 找家族
    /// </summary>
    /// <param name="id">家族 id</param>
    /// <returns>家族；没有为 null</returns>
    public ImageCatalogFamily? Find(string? id) => Families.FirstOrDefault(x => x.Id == id);

    /// <summary>
    /// 解析一份清单 JSON
    /// </summary>
    /// <param name="json">JSON 文本</param>
    /// <returns>清单</returns>
    public static ImageModelCatalog Parse(string json) =>
        JsonSerializer.Deserialize(json, ImageModelCatalogJsonContext.Default.ImageModelCatalog)
        ?? throw new InvalidDataException("Image model catalog is empty.");

    private static ImageModelCatalog LoadBuiltIn()
    {
        using Stream stream = typeof(ImageModelCatalog).Assembly.GetManifestResourceStream(ResourceName)
                              ?? throw new InvalidOperationException($"Missing resource {ResourceName}.");
        using StreamReader reader = new(stream);
        return Parse(reader.ReadToEnd());
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ImageModelCatalog))]
internal sealed partial class ImageModelCatalogJsonContext : JsonSerializerContext;
