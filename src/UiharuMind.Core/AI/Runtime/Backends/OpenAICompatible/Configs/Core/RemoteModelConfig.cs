using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

public class RemoteModelConfig : BaseRemoteModelConfig
{
    // public override string? ConfigType { get; set; } = typeof(RemoteModelConfig).FullName;

    public override string ModelName { get; set; } = "";

    public override string ModelPath { get; set; } = "";

    public override string ModelDescription { get; set; } = "";

    public override string ModelId { get; set; } = "";

    public override int Port { get; set; }
}