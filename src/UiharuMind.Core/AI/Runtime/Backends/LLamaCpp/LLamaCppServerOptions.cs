namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// llama-server 专有的启动选项（与引擎无关的上下文、GPU 层数等在 ModelRuntimeSettingConfig）。
/// 默认值与 llama-server 自己的默认一致，等于默认的项不传，让上游的默认（含 --fit 自动适配内存）生效
/// </summary>
public sealed class LLamaCppServerOptions
{
    /// <summary>
    /// llama-server 的 KV 缓存默认类型
    /// </summary>
    public const string DefaultCacheType = "f16";

    /// <summary>
    /// llama-server 的加载方式默认值
    /// </summary>
    public const string DefaultLoadMode = "auto";

    /// <summary>
    /// 加载方式可选值（--load-mode，上游已用它取代 --mlock / --no-mmap）
    /// </summary>
    public static readonly string[] LoadModes = ["auto", "mmap", "mmap+mlock", "mlock", "none", "dio"];

    /// <summary>
    /// KV 缓存可选类型
    /// </summary>
    public static readonly string[] CacheTypes = ["f16", "bf16", "f32", "q8_0", "q5_1", "q5_0", "iq4_nl", "q4_1", "q4_0"];

    /// <summary>
    /// 按设备内存自动调整未设置的参数（--fit）
    /// </summary>
    public bool Fit { get; set; } = true;

    /// <summary>
    /// --fit 给每个设备留的余量（MiB）
    /// </summary>
    public int FitTargetMiB { get; set; } = 1024;

    /// <summary>
    /// 处理提示词的线程数，0 跟随生成线程
    /// </summary>
    public int ThreadsBatch { get; set; }

    /// <summary>
    /// 并行槽位数，0 交给 llama-server 自动
    /// </summary>
    public int Parallel { get; set; }

    /// <summary>
    /// 连续批处理
    /// </summary>
    public bool ContinuousBatching { get; set; } = true;

    /// <summary>
    /// KV 缓存 K 的数据类型
    /// </summary>
    public string CacheTypeK { get; set; } = DefaultCacheType;

    /// <summary>
    /// KV 缓存 V 的数据类型
    /// </summary>
    public string CacheTypeV { get; set; } = DefaultCacheType;

    /// <summary>
    /// 模型加载方式：内存映射、锁定内存、全部读入等
    /// </summary>
    public string LoadMode { get; set; } = DefaultLoadMode;

    /// <summary>
    /// KV 缓存留在内存，不放显存
    /// </summary>
    public bool NoKvOffload { get; set; }

    /// <summary>
    /// 前 N 层的 MoE 专家权重留在 CPU
    /// </summary>
    public int CpuMoeLayers { get; set; }

    /// <summary>
    /// 提示词缓存上限（MiB），-1 不限，0 关闭
    /// </summary>
    public int CacheRamMiB { get; set; } = 8192;

    /// <summary>
    /// 复用缓存前缀的最小块，0 关闭
    /// </summary>
    public int CacheReuse { get; set; }

    /// <summary>
    /// 上下文满了时丢掉开头继续生成
    /// </summary>
    public bool ContextShift { get; set; }

    /// <summary>
    /// 滑动窗口注意力模型用完整大小的 KV 缓存
    /// </summary>
    public bool SwaFull { get; set; }

    /// <summary>
    /// 加载超时（秒）
    /// </summary>
    public int LoadTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// 额外环境变量，KEY=VALUE 以分号或换行分隔
    /// </summary>
    public string EnvironmentVariables { get; set; } = "";

    /// <summary>
    /// 追加在最后的启动参数（同名参数以它为准）
    /// </summary>
    public string ExtraArguments { get; set; } = "";
}
