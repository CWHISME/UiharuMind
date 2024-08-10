using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.LLamaCpp.Data;

public class LLamaCppSettingConfig : ConfigBase
{
    /// <summary>
    /// 默认运行端口
    /// </summary>
    public int DefautPort { get; set; } = 1369;

    public string? LLamaCppPath { get; set; }

    public string LocalModelPath { get; set; } = "./Models";

    public Dictionary<string, GGufModelInfo> ModelInfos { get; set; } = new();

    public string ExeLookupStats => Path.Combine(LLamaCppPath!, "lookup-stats");
    public string ExeServer => Path.Combine(LLamaCppPath!, "server");
}