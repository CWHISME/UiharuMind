using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 本地生图模型与 sd.cpp 引擎之间的换算：从可下载列表建条目、恢复推荐值、换成启动描述与请求
/// </summary>
public static class LocalImageModels
{
    private const int SizeMultiple = 64;
    private const int MinEdge = 256;
    private const int MaxEdge = 2048;

    /// <summary>
    /// 本地模型的组件放在哪（与对话模型同一个模型目录）
    /// </summary>
    public static string ModelRoot => ModelSettingConfig.Current.LocalModelPath;

    /// <summary>
    /// 从可下载列表的某一档建一个生图模型条目
    /// </summary>
    /// <param name="family">家族</param>
    /// <param name="tier">档位</param>
    /// <param name="name">条目名（列表内唯一）</param>
    /// <returns>条目</returns>
    public static ImageModelInfo Create(ImageCatalogFamily family, ImageCatalogTier tier, string name)
    {
        LocalImageModel local = new()
        {
            Family = family.Id,
            Tier = tier.Id,
            Components = tier.Components.Select(x => new LocalImageComponent
                { Flag = x.Flag, Repository = x.Repo, File = x.File }).ToList()
        };
        ApplyRecommended(local, family);
        return new ImageModelInfo
        {
            Name = name,
            Dialect = EImageDialect.Local,
            Local = local,
            SupportsEditing = family.SupportsEditing
        };
    }

    /// <summary>
    /// 采样参数、基准边长、负向提示词、固定参数取回家族的推荐值
    /// </summary>
    /// <param name="local">本地模型</param>
    /// <param name="family">家族</param>
    public static void ApplyRecommended(LocalImageModel local, ImageCatalogFamily family)
    {
        local.ExtraArguments = family.ExtraArguments.ToList();
        local.BaseResolution = family.BaseResolution;
        local.NegativePrompt = family.NegativePrompt;
        local.Steps = family.Sampling.Steps;
        local.CfgScale = family.Sampling.Cfg;
        local.Sampler = family.Sampling.Sampler;
        local.Scheduler = family.Sampling.Scheduler;
        local.FlowShift = family.Sampling.FlowShift;
        local.DistilledGuidance = family.Sampling.DistilledGuidance;
    }

    /// <summary>
    /// 怎么起 sd-server
    /// </summary>
    /// <param name="model">生图模型（本地的）</param>
    /// <param name="modelRoot">模型目录</param>
    /// <returns>启动描述</returns>
    public static SdCppModelSetup ToSetup(ImageModelInfo model, string modelRoot)
    {
        LocalImageModel local = model.Local ?? throw new InvalidOperationException($"'{model.Name}' is not a local model.");
        return new SdCppModelSetup(model.Name,
            local.Components.Select(x => new SdCppComponent(x.Flag, x.Resolve(modelRoot))).ToList(),
            local.ExtraArguments);
    }

    /// <summary>
    /// 缺了哪些组件文件
    /// </summary>
    /// <param name="model">生图模型（本地的）</param>
    /// <param name="modelRoot">模型目录</param>
    /// <returns>缺的文件；齐了为空</returns>
    public static IReadOnlyList<string> MissingFiles(ImageModelInfo model, string modelRoot) =>
        model.Local == null ? [] : ToSetup(model, modelRoot).MissingFiles();

    /// <summary>
    /// 组件文件在本机一共占多大（同一文件只算一次，缺的不算）
    /// </summary>
    /// <param name="model">生图模型（本地的）</param>
    /// <param name="modelRoot">模型目录</param>
    /// <returns>字节数；不是本地模型或一个都没有为 0</returns>
    public static long SizeOnDisk(ImageModelInfo model, string modelRoot) => model.Local == null
        ? 0
        : model.Local.Components.Select(x => x.Resolve(modelRoot)).Distinct()
            .Select(x => new FileInfo(x)).Where(x => x.Exists).Sum(x => x.Length);

    /// <summary>
    /// 把出图请求换成引擎的请求：宽高按比例保持基准面积，改图没给比例就跟主图
    /// </summary>
    /// <param name="local">本地模型</param>
    /// <param name="request">出图请求</param>
    /// <returns>引擎请求</returns>
    public static SdCppImageRequest ToRequest(LocalImageModel local, ImageRequest request)
    {
        ImageAspectRatio ratio = request.AspectRatio ?? MainImageRatio(request) ?? ImageAspectRatio.Square;
        (int width, int height) = ImageSizing.ToPixels(ratio, local.BaseResolution, SizeMultiple, MinEdge, MaxEdge);
        return new SdCppImageRequest(request.Prompt, width, height,
            new SdCppSampling(local.Steps, local.CfgScale, local.Sampler, local.Scheduler, local.FlowShift,
                local.DistilledGuidance))
        {
            NegativePrompt = local.NegativePrompt,
            ReferenceImages = request.Images.Select(x => x.Bytes).ToList()
        };
    }

    private static ImageAspectRatio? MainImageRatio(ImageRequest request) =>
        request.Images.Count > 0 && ImageFormats.TryReadSize(request.Images[0].Bytes, out int width, out int height)
            ? ImageAspectRatio.Nearest(width, height)
            : null;
}
