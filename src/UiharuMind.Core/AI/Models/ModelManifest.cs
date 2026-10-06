using UiharuMind.Core.Core;

namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 模型清单：仓库目录（&lt;模型目录&gt;/&lt;作者&gt;/&lt;仓库&gt;/）里的旁挂文件，记下从哪个模型源的哪个仓库来、
/// 各文件的校验值、视觉投影配给谁。有清单的是「下载的模型」，没有的是「手放的模型」。
/// </summary>
public sealed class ModelManifest
{
    /// <summary>
    /// 清单文件名
    /// </summary>
    public const string FileName = "uiharu-manifest.json";

    /// <summary>
    /// 格式版本
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// 模型源 Id（如 huggingface、modelscope）
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// 仓库名，形如 owner/repo
    /// </summary>
    public string Repository { get; set; } = "";

    /// <summary>
    /// 仓库版本（分支或提交）
    /// </summary>
    public string Revision { get; set; } = "main";

    /// <summary>
    /// 这个目录里下载的模型，同一仓库可以下多个量化
    /// </summary>
    public List<ManifestModel> Models { get; set; } = [];

    /// <summary>
    /// 这个目录里下载的视觉投影（mmproj）
    /// </summary>
    public List<ManifestFile> Projectors { get; set; } = [];

    /// <summary>
    /// 读取目录里的清单
    /// </summary>
    /// <param name="directory">仓库目录</param>
    /// <returns>清单；没有或损坏为 null</returns>
    public static ModelManifest? TryLoad(string directory)
    {
        return SaveUtility.Load<ModelManifest>(Path.Combine(directory, FileName), SaveUtility.JsonOptions);
    }

    /// <summary>
    /// 原子写入目录
    /// </summary>
    /// <param name="directory">仓库目录</param>
    public void Save(string directory)
    {
        SaveUtility.Save(Path.Combine(directory, FileName), this, SaveUtility.JsonOptions);
    }

    /// <summary>
    /// 按主文件（分片模型为第一片）找模型
    /// </summary>
    /// <param name="fileName">相对仓库目录的文件路径</param>
    /// <returns>模型；没有为 null</returns>
    public ManifestModel? FindModel(string fileName)
    {
        return Models.FirstOrDefault(x =>
            x.Files.Count > 0 && string.Equals(x.Files[0].Path, fileName, StringComparison.Ordinal));
    }
}

/// <summary>
/// 清单里的一个模型
/// </summary>
public sealed class ManifestModel
{
    /// <summary>
    /// 组成文件，分片模型按片序
    /// </summary>
    public List<ManifestFile> Files { get; set; } = [];

    /// <summary>
    /// 配给它的视觉投影，相对仓库目录；没有为 null
    /// </summary>
    public string? Projector { get; set; }

    /// <summary>
    /// 按模型的运行参数覆写，空字段跟随全局
    /// </summary>
    public ModelRuntimeOverrides? Overrides { get; set; }
}

/// <summary>
/// 清单里的一个文件
/// </summary>
public sealed class ManifestFile
{
    /// <summary>
    /// 相对仓库目录的路径
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// 字节数
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 小写十六进制 sha256；模型源没给时为空
    /// </summary>
    public string Sha256 { get; set; } = "";
}

/// <summary>
/// 按模型的运行参数覆写（预留，暂无界面）
/// </summary>
public sealed class ModelRuntimeOverrides
{
    /// <summary>
    /// 上下文长度
    /// </summary>
    public int? ContextSize { get; set; }

    /// <summary>
    /// GPU 层数
    /// </summary>
    public int? GpuLayers { get; set; }
}
