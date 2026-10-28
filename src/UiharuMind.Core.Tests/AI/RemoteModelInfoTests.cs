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

    /// <summary>roundtrip 三态：DeepSeek/通用未声明(null=自动)；sensenova 自研显式 false</summary>
    [Fact]
    public void ReplayExistingReasoning_IsTriState()
    {
        var deepseek = new RemoteModelInfo
        {
            Config = new RemoteDeepSeekModelConfig { ModelId = "deepseek-v4-flash" },
        };
        var generic = new RemoteModelInfo { Config = new RemoteModelConfig() };
        var sensenova = new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" } };

        Assert.Null(deepseek.ReplayExistingReasoning); // 默认 auto：有正文就回传
        Assert.Null(generic.ReplayExistingReasoning);
        Assert.False(sensenova.ReplayExistingReasoning);
    }

    /// <summary>
    /// 回填键名（请求侧）只看预设表与默认：<b>观测值不参与</b>。
    /// 观测到的是响应里的字段名，请求侧未必同名——CommandCode 响应给 <c>reasoning</c>、
    /// 请求只认 <c>reasoning_content</c>，拿观测值回填等于发一个服务端不认的键，被当缺字段 400。
    /// </summary>
    [Fact]
    public void ThinkingField_FallsBackPresetThenDefault_IgnoringObservation()
    {
        // 观测值再花哨也不影响请求侧键名
        var observed = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig
            {
                ModelId = "sensenova-6.8-flash-lite",
                ObservedThinkingFields = { ["sensenova-6.8-flash-lite"] = "reasoning_text" },
            },
        };
        Assert.Equal("reasoning", observed.ThinkingField); // 预设表声明的那个

        // 预设表(sensenova 自研思考字段是 reasoning)
        var preseted = new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" } };
        Assert.Equal("reasoning", preseted.ThinkingField);

        // 默认 reasoning_content
        var generic = new RemoteModelInfo { Config = new RemoteModelConfig { ModelId = "anything" } };
        Assert.Equal("reasoning_content", generic.ThinkingField);

        // 无预设、观测到 reasoning 的网关：请求仍走默认键
        var gateway = new RemoteModelInfo
        {
            Config = new RemoteCommandCodeModelConfig
            {
                ModelId = "reflection/beam:free",
                ObservedThinkingFields = { ["reflection/beam:free"] = "reasoning" },
            },
        };
        Assert.Equal("reasoning_content", gateway.ThinkingField);
    }

    /// <summary>观测值按 ModelId 隔离：同端点从 6.8 切到 DeepSeek，各记各的，不串台</summary>
    [Fact]
    public void ThinkingField_ObservedOnOneModelId_DoesNotLeakToAnother()
    {
        var config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" };
        config.ObservedThinkingFields["sensenova-6.8-flash-lite"] = "reasoning";
        Assert.Equal("reasoning", config.ObservedThinkingFields["sensenova-6.8-flash-lite"]);

        config.ModelId = "deepseek-v4-flash";
        Assert.False(config.ObservedThinkingFields.ContainsKey("deepseek-v4-flash"));
        Assert.Equal("reasoning_content", config.RequestThinkingField);
    }

    /// <summary>观测字段名随配置序列化落盘,读回保留;旧配置缺字段=空表,查表兜底</summary>
    [Fact]
    public void ThinkingField_RoundTripsThroughSerialization()
    {
        var config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" };
        config.ObservedThinkingFields["sensenova-6.8-flash-lite"] = "reasoning";

        string json = JsonSerializer.Serialize(config, SaveUtility.SaveJsonOptions);
        var restored = JsonSerializer.Deserialize<BaseRemoteModelConfig>(json, SaveUtility.SaveJsonOptions);

        Assert.Equal("reasoning", restored!.ObservedThinkingFields["sensenova-6.8-flash-lite"]);

        // 旧配置没有该字段:反序列化后为空表
        var legacy = JsonSerializer.Deserialize<BaseRemoteModelConfig>(
            """{"ConfigType":"RemoteSensenovaModelConfig"}""", SaveUtility.SaveJsonOptions);
        Assert.Empty(legacy!.ObservedThinkingFields);
        Assert.Equal("reasoning_content", legacy.RequestThinkingField);
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

        Assert.Equal("reasoning", restored.ModelInfos["sensenova"].Config.ObservedThinkingFields["sensenova-6.8-flash-lite"]);
    }

    /// <summary>
    /// 从 400 里学到的「每条助手消息都要带思考」随配置落盘：重启后不该让用户再吃一次 400。
    /// </summary>
    [Fact]
    public void LearnedReasoningRequirement_SurvivesConfigFileRoundTrip()
    {
        var settings = new RemoteModelSettingConfig();
        var info = new RemoteModelInfo
        {
            Config = new RemoteCommandCodeModelConfig { ModelName = "cc", ModelId = "reflection/beam:free" },
        };
        Assert.True(info.Config.LearnRequireReasoningOnEveryAssistantMessage());
        settings.ModelInfos["cc"] = info;

        string json = JsonSerializer.Serialize(settings, SaveUtility.SaveJsonOptions);
        var restored = JsonSerializer.Deserialize<RemoteModelSettingConfig>(json, SaveUtility.SaveJsonOptions)!;

        BaseRemoteModelConfig config = restored.ModelInfos["cc"].Config;
        Assert.Contains("reflection/beam:free", config.ReasoningEveryAssistantModelIds);
        Assert.True(config.RequireReasoningOnEveryAssistantMessage);
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
