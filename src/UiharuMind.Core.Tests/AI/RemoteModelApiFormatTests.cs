using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// wire 字段（ApiFormat）的存档形状：默认 ChatCompletions、往返保留、
/// 旧存档没有该字段时落到默认值——v1 只实现 chat/completions 线，字段为二期 Anthropic 预留。
/// </summary>
public class RemoteModelApiFormatTests
{
    [Fact]
    public void NewConfig_DefaultsToChatCompletions()
    {
        var config = new RemoteModelConfig();

        Assert.Equal(EApiFormat.ChatCompletions, config.ApiFormat);
    }

    [Fact]
    public void RoundTrip_ThroughBaseType_KeepsApiFormat()
    {
        var config = new RemoteModelConfig { ApiFormat = EApiFormat.Messages };

        string json = SaveUtility.SaveToString<BaseRemoteModelConfig>(config);
        RemoteModelConfig restored = SaveUtility.LoadFromString<RemoteModelConfig>(json);

        Assert.Equal(EApiFormat.Messages, restored.ApiFormat);
    }

    [Fact]
    public void OldArchive_WithoutApiFormat_FallsBackToChatCompletions()
    {
        const string oldJson = """{ "ConfigType": "RemoteModelConfig", "ModelId": "x" }""";

        RemoteModelConfig restored = SaveUtility.LoadFromString<RemoteModelConfig>(oldJson);

        Assert.Equal(EApiFormat.ChatCompletions, restored.ApiFormat);
    }
}
