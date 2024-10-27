using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.RemoteOpenAI;

public class RemoteModelSettingConfig : ConfigBase
{
    public Dictionary<string, RemoteModelInfo> ModelInfos { get; set; } = new();
}