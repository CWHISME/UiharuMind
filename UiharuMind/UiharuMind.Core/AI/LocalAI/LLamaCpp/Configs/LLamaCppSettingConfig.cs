using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.LLamaCpp.Data;

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

public class LLamaCppSettingConfig : ConfigBase
{
    public const string ServerExeName = "llama-server";

    /// <summary>
    /// 默认运行端口
    /// </summary>
    public int DefautPort { get; set; } = 1369;

    public string? LLamaCppPath { get; set; }

    public string LocalModelPath { get; set; } = "./Models";

    public Dictionary<string, GGufModelInfo> ModelInfos { get; set; } = new();

    public string ExeLookupStats => Path.Combine(LLamaCppPath!, "llama-lookup-stats");
    public string ExeServer => Path.Combine(LLamaCppPath!, ServerExeName);

    public LLamaCppServerConfig ServerConfig { get; set; } = new();
    public LLamaCppSamplingStrategiesConfig SamplingStrategiesConfig { get; set; } = new();
}