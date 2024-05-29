using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.LLamaCpp.Data;

public class LLamaCppSettingConfig : ConfigBase
{
    public string? LLamaCppPath { get; set; }
    
    public string LocalModelPath { get; set; } = "./Models";

    public Dictionary<string, GGufModelInfo> ModelInfos { get; set; } = new();
}