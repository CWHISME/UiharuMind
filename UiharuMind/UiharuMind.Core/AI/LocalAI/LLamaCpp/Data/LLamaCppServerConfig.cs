using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.LLamaCpp.Data;

public class LLamaCppServerConfig : ConfigBase
{
    
    //常规配置
    //是否显示所有日志
    public bool LogVerbose { get; set; }
    //日志前缀
    public bool LogPrefix { get; set; }
    
    public int Threads { get; set; }
    public string CpuMask { get; set; }
    public string CpuRange { get; set; }
    public bool CpuStrict  { get; set; }
    public int Priority { get; set; }
    public int ContextSize { get; set; }
    public int TokensToPredict { get; set; }
    public int BatchSize { get; set; }
    public int PhysicalBatchSize { get; set; }
    public bool EnableFlashAttention { get; set; }
    public int RoPEScalingMethod { get; set; }
    public double RoPEContextScalingFactor { get; set; }
    public double RoPEBaseFrequency { get; set; }
    public int YaRNOriginalContextSize { get; set; }
    public double YaRNextrapolationMixFactor { get; set; }
    public double YaRNAttentionFactor { get; set; }
    public double YaRNBetaSlow { get; set; }
    public double YaRNBetaFast { get; set; }
    public int GpuLayers { get; set; }
    public int SplitMode { get; set; }
    public string TensorSplit { get; set; }
    public int MainGpuIndex { get; set; }   
    public bool CheckModelTensors { get; set; }
}