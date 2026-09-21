using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.RemoteOpenAI;

public class RemoteModelSettingConfig : TConfigBase<RemoteModelSettingConfig>
{
    public Dictionary<string, RemoteModelInfo> ModelInfos { get; set; } = new();
}