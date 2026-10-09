using UiharuMind.Core.AI.Models.Sources;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 本地生图模型的一个组件。从可下载列表下的记仓库与文件（路径随模型目录走）；自定义组合记手放文件的路径
/// </summary>
public sealed class LocalImageComponent
{
    /// <summary>
    /// 交给 sd-server 的参数名（不带横线）
    /// </summary>
    public string Flag { get; set; } = "";

    /// <summary>
    /// owner/repo；手放的为 null
    /// </summary>
    public string? Repository { get; set; }

    /// <summary>
    /// 仓库内路径；手放的为 null
    /// </summary>
    public string? File { get; set; }

    /// <summary>
    /// 手放文件的绝对路径；从仓库下的为 null
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// 文件在本机的位置
    /// </summary>
    /// <param name="modelRoot">模型目录</param>
    /// <returns>绝对路径</returns>
    public string Resolve(string modelRoot) => Path ?? ModelRepoDownloader.FilePath(
        ModelRepoDownloader.RepoDirectory(modelRoot, Repository ?? ""), new ModelRepoFile(File ?? "", 0, null));

    /// <summary>
    /// 复制一份
    /// </summary>
    /// <returns>副本</returns>
    public LocalImageComponent Clone() => (LocalImageComponent)MemberwiseClone();
}

/// <summary>
/// 生图模型列表里本地的那种（<see cref="EImageDialect.Local"/>）：用哪些组件，以及在用的采样参数。
/// 从可下载列表装的记着家族与档位，「恢复推荐」按它取回推荐值
/// </summary>
public sealed class LocalImageModel
{
    /// <summary>
    /// 可下载列表里的家族 id；自定义组合为 null
    /// </summary>
    public string? Family { get; set; }

    /// <summary>
    /// 档位 id；自定义组合为 null
    /// </summary>
    public string? Tier { get; set; }

    /// <summary>
    /// 组件
    /// </summary>
    public List<LocalImageComponent> Components { get; set; } = [];

    /// <summary>
    /// 这个家族要的固定启动参数
    /// </summary>
    public List<string> ExtraArguments { get; set; } = [];

    /// <summary>
    /// 基准边长：按比例换算宽高时面积取它的平方
    /// </summary>
    public int BaseResolution { get; set; } = 1024;

    /// <summary>
    /// 负向提示词
    /// </summary>
    public string NegativePrompt { get; set; } = "";

    /// <summary>
    /// 步数
    /// </summary>
    public int Steps { get; set; } = 20;

    /// <summary>
    /// CFG
    /// </summary>
    public float CfgScale { get; set; } = 7f;

    /// <summary>
    /// 采样器；null 交给 sd.cpp
    /// </summary>
    public string? Sampler { get; set; }

    /// <summary>
    /// 调度器；null 交给 sd.cpp
    /// </summary>
    public string? Scheduler { get; set; }

    /// <summary>
    /// flow shift；null 交给 sd.cpp
    /// </summary>
    public float? FlowShift { get; set; }

    /// <summary>
    /// 蒸馏引导；null 交给 sd.cpp
    /// </summary>
    public float? DistilledGuidance { get; set; }

    /// <summary>
    /// 深拷一份：改参数在副本上改，生图模型列表写时复制
    /// </summary>
    /// <returns>副本</returns>
    public LocalImageModel Clone()
    {
        LocalImageModel copy = (LocalImageModel)MemberwiseClone();
        copy.Components = Components.Select(x => x.Clone()).ToList();
        copy.ExtraArguments = [..ExtraArguments];
        return copy;
    }
}
