using System.Text.Json.Serialization;

namespace UiharuMind.Core.Configs;

/// <summary>
/// 某个本地模型自己的运行参数，null 的项跟随全局（ModelRuntimeSettingConfig）
/// </summary>
public sealed class ModelRuntimeOverrides
{
    /// <summary>
    /// 上下文长度，0 自动
    /// </summary>
    public int? ContextSize { get; set; }

    /// <summary>
    /// GPU 层数：负数自动，0 纯 CPU
    /// </summary>
    public int? GpuLayers { get; set; }

    /// <summary>
    /// 逻辑批大小，0 自动
    /// </summary>
    public int? BatchSize { get; set; }

    /// <summary>
    /// 物理批大小，0 自动
    /// </summary>
    public int? UBatchSize { get; set; }

    /// <summary>
    /// CPU 线程数，0 自动
    /// </summary>
    public int? Threads { get; set; }

    /// <summary>
    /// 全部跟随全局
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => ContextSize == null && GpuLayers == null && BatchSize == null && UBatchSize == null &&
                           Threads == null;
}
