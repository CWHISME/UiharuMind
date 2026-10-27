using System.Text.Json;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.RemoteOpenAI;

namespace UiharuMind.Core.Tests.AI;

public class RemoteModelInfoTests
{
    private const string TestApiKey = "f30f9148bd0aaba38fe60077e89df104.uhnv7eX1ctIs8XOK";

    [Fact]
    public void ApiKey_SerializedValue_IsEncryptedNotPlaintext()
    {
        var info = new RemoteModelInfo { ApiKey = TestApiKey };

        string json = JsonSerializer.Serialize(info, SaveUtility.SaveJsonOptions);

        Assert.DoesNotContain(TestApiKey, json);
        Assert.Contains("\"ApiKey\"", json);
    }

    [Fact]
    public void ApiKey_RoundTrip_ThroughSerialization()
    {
        var info = new RemoteModelInfo { ApiKey = TestApiKey };

        string json = JsonSerializer.Serialize(info, SaveUtility.SaveJsonOptions);
        var restored = JsonSerializer.Deserialize<RemoteModelInfo>(json, SaveUtility.SaveJsonOptions);

        Assert.Equal(TestApiKey, restored!.ApiKey);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        string encrypted = AesEncryptionUtils.EncryptString(TestApiKey);

        Assert.NotEqual(TestApiKey, encrypted);
        Assert.Equal(TestApiKey, AesEncryptionUtils.DecryptString(encrypted));
    }

    [Fact]
    public void DecryptString_InvalidCipherText_ReturnsEmpty()
    {
        Assert.Equal("", AesEncryptionUtils.DecryptString("not-a-valid-cipher"));
    }

    [Fact]
    public void ApiKey_DifferentValues_ProduceDifferentCipherText()
    {
        string a = AesEncryptionUtils.EncryptString("key-one");
        string b = AesEncryptionUtils.EncryptString("key-two");

        Assert.NotEqual(a, b);
    }

    /// <summary>roundtrip 三态：DeepSeek 显式 true；通用配置未声明(null=自动)；sensenova 自研显式 false</summary>
    [Fact]
    public void RequiresReasoningContentRoundtrip_IsTriState()
    {
        var deepseek = new RemoteModelInfo
        {
            Config = new RemoteDeepSeekModelConfig { ModelId = "deepseek-v4-flash" },
        };
        var generic = new RemoteModelInfo { Config = new RemoteModelConfig() };
        var sensenova = new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" } };

        Assert.True(deepseek.RequiresReasoningContentRoundtrip);
        Assert.Null(generic.RequiresReasoningContentRoundtrip);
        Assert.False(sensenova.RequiresReasoningContentRoundtrip);
    }

    /// <summary>回传键名三级查表：当前 ModelId 的观测值 → 预设表 → reasoning_content</summary>
    [Fact]
    public void ThinkingField_FallsBackPresetThenDefault()
    {
        // 观测值优先(不管预设写什么)
        var observed = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig
            {
                ModelId = "sensenova-6.8-flash-lite",
                ObservedThinkingFields = { ["sensenova-6.8-flash-lite"] = "reasoning_text" },
            },
        };
        Assert.Equal("reasoning_text", observed.ThinkingField);

        // 预设表(sensenova 自研思考字段是 reasoning)
        var preseted = new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" } };
        Assert.Equal("reasoning", preseted.ThinkingField);

        // 默认 reasoning_content
        var generic = new RemoteModelInfo { Config = new RemoteModelConfig { ModelId = "anything" } };
        Assert.Equal("reasoning_content", generic.ThinkingField);
    }

    /// <summary>
    /// 观测值按 ModelId 隔离：同端点从 6.8 切到 DeepSeek，前者的观测不能压住后者的预设/默认，
    /// 否则 DeepSeek 会收到它不认的 reasoning 键（商汤同端点两个模型字段名相反）
    /// </summary>
    [Fact]
    public void ThinkingField_ObservedOnOneModelId_DoesNotLeakToAnother()
    {
        var config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" };
        config.ObservedThinkingFields["sensenova-6.8-flash-lite"] = "reasoning";
        Assert.Equal("reasoning", config.EffectiveThinkingField);

        config.ModelId = "deepseek-v4-flash";
        Assert.Equal("reasoning_content", config.EffectiveThinkingField);
    }

    /// <summary>观测字段名随配置序列化落盘,读回保留;旧配置缺字段=空表,查表兜底</summary>
    [Fact]
    public void ThinkingField_RoundTripsThroughSerialization()
    {
        var config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" };
        config.ObservedThinkingFields["sensenova-6.8-flash-lite"] = "reasoning";

        string json = JsonSerializer.Serialize(config, SaveUtility.SaveJsonOptions);
        var restored = JsonSerializer.Deserialize<BaseRemoteModelConfig>(json, SaveUtility.SaveJsonOptions);

        Assert.Equal("reasoning", restored!.EffectiveThinkingField);

        // 旧配置没有该字段:反序列化后为空表,有效值回退默认(反序列化回基类后无预设表可查)
        var legacy = JsonSerializer.Deserialize<BaseRemoteModelConfig>(
            """{"ConfigType":"RemoteSensenovaModelConfig"}""", SaveUtility.SaveJsonOptions);
        Assert.Empty(legacy!.ObservedThinkingFields);
        Assert.Equal("reasoning_content", legacy.EffectiveThinkingField);
    }

    /// <summary>
    /// 观测值随整份远程模型配置落盘：保存走 <see cref="RemoteModelSettingConfig"/> 的整体序列化
    /// （类型图按 <see cref="BaseRemoteModelConfig"/> 走），所以字段必须声明在基类上——
    /// 落在子类会被静默丢掉，重启后历史会话的思考就按错键回填了。
    /// </summary>
    [Fact]
    public void ObservedThinkingFields_SurviveConfigFileRoundTrip()
    {
        var settings = new RemoteModelSettingConfig();
        var info = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig { ModelName = "sensenova", ModelId = "sensenova-6.8-flash-lite" },
        };
        info.Config.ObservedThinkingFields["sensenova-6.8-flash-lite"] = "reasoning";
        settings.ModelInfos["sensenova"] = info;

        string json = JsonSerializer.Serialize(settings, SaveUtility.SaveJsonOptions);
        var restored = JsonSerializer.Deserialize<RemoteModelSettingConfig>(json, SaveUtility.SaveJsonOptions)!;

        Assert.Equal("reasoning", restored.ModelInfos["sensenova"].Config.EffectiveThinkingField);
    }

    [Fact]
    public void OmitSamplingParams_ForwardsConfigValue()
    {
        var kimi = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig { ModelId = "kimi-k3", OmitSamplingParams = true },
        };
        var generic = new RemoteModelInfo { Config = new RemoteModelConfig() };

        Assert.True(kimi.OmitSamplingParams);
        Assert.False(generic.OmitSamplingParams);
    }

    [Fact]
    public void WebsiteUrl_PresetProvidersHaveOne_CustomDoesNot()
    {
        Assert.Equal("https://platform.deepseek.com", new RemoteDeepSeekModelConfig().WebsiteUrl);
        Assert.Equal("https://open.bigmodel.cn", new RemoteZhipuModelConfig().WebsiteUrl);
        Assert.Equal("https://platform.sensenova.cn", new RemoteSensenovaModelConfig().WebsiteUrl);
        Assert.Equal(string.Empty, new RemoteModelConfig().WebsiteUrl);
    }
}
