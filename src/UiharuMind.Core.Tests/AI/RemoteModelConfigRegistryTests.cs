using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 显式注册表替代反射扫描后的形状等价断言，三组：
/// 供应商列表组成与顺序、全局上下文变体表关键条目、ConfigType 字符串解析。
/// 这是原反射装配行为的地基，注册表改动不得改变它们。
/// </summary>
public class RemoteModelConfigRegistryTests
{
    [Fact]
    public void ProviderTypes_AreTheFourVendorsInFixedOrder()
    {
        Assert.Equal(
            [typeof(RemoteAgnesModelConfig), typeof(RemoteDeepSeekModelConfig),
             typeof(RemoteSensenovaModelConfig), typeof(RemoteZhipuModelConfig)],
            RemoteModelConfigRegistry.ProviderTypes);
    }

    [Fact]
    public void ProviderTypes_AllImplementIRemoteModelConfig()
    {
        Assert.All(RemoteModelConfigRegistry.ProviderTypes,
            type => Assert.True(typeof(IRemoteModelConfig).IsAssignableFrom(type)));
    }

    [Fact]
    public void AllConfigTypes_IncludeGenericFallbackLast()
    {
        Assert.Equal(5, RemoteModelConfigRegistry.AllConfigTypes.Count);
        Assert.Equal(typeof(RemoteModelConfig), RemoteModelConfigRegistry.AllConfigTypes[^1]);
    }

    [Fact]
    public void CreateDefault_ProducesUsableConfigForEveryRegisteredType()
    {
        foreach (Type type in RemoteModelConfigRegistry.AllConfigTypes)
        {
            BaseRemoteModelConfig? config = RemoteModelConfigRegistry.CreateDefault(type);
            Assert.NotNull(config);
            Assert.Equal(type.Name, config.ConfigType);
        }
    }

    [Fact]
    public void CreateDefault_UnknownType_ReturnsNull()
    {
        Assert.Null(RemoteModelConfigRegistry.CreateDefault(typeof(string)));
    }

    [Theory]
    [InlineData("RemoteAgnesModelConfig", typeof(RemoteAgnesModelConfig))]
    [InlineData("RemoteDeepSeekModelConfig", typeof(RemoteDeepSeekModelConfig))]
    [InlineData("RemoteSensenovaModelConfig", typeof(RemoteSensenovaModelConfig))]
    [InlineData("RemoteZhipuModelConfig", typeof(RemoteZhipuModelConfig))]
    [InlineData("RemoteModelConfig", typeof(RemoteModelConfig))]
    public void ResolveConfigType_ByName_FindsConfig(string name, Type expected)
    {
        Assert.Equal(expected, RemoteModelConfigRegistry.ResolveConfigType(name));
    }

    [Fact]
    public void ResolveConfigType_ByFullName_FindsConfig()
    {
        Assert.Equal(typeof(RemoteDeepSeekModelConfig),
            RemoteModelConfigRegistry.ResolveConfigType(typeof(RemoteDeepSeekModelConfig).FullName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("RemoteDeletedConfig")]
    [InlineData("Some.Namespace.RemoteZhipuModelConfig")]
    public void ResolveConfigType_UnknownOrEmpty_ReturnsNull(string? name)
    {
        Assert.Null(RemoteModelConfigRegistry.ResolveConfigType(name));
    }

    /// <summary>全局上下文表（ModelContextResolver 的变体表）不因改注册表而变：关键模型 ID 的值逐一钉死</summary>
    [Theory]
    [InlineData("agnes-3.0-flash", 524288)]
    [InlineData("deepseek-v4-pro", 1048576)]
    [InlineData("sensenova-6.8-flash-lite", 262144)]
    [InlineData("kimi-k3", 1048576)]
    [InlineData("glm-4.7-flash", 204800)]
    [InlineData("glm-5v-turbo", 204800)]
    [InlineData("glm-4v-flash", 16384)]
    [InlineData("glm-4", 128000)]
    public void GlobalVariantTable_KeepsKnownModelContexts(string modelId, int contextLength)
    {
        Assert.Equal(contextLength, ModelContextResolver.ResolveRemote(0, modelId, null));
    }

    /// <summary>每家的变体表条目在全局表里一个都不能丢（先到先得合并后仍是超集）</summary>
    [Fact]
    public void GlobalVariantTable_CoversEveryProviderEntry()
    {
        foreach (Type type in RemoteModelConfigRegistry.ProviderTypes)
        {
            BaseRemoteModelConfig config = RemoteModelConfigRegistry.CreateDefault(type)!;
            foreach (string modelId in config.ModelIdVariants.Keys)
            {
                Assert.NotEqual(ModelContextResolver.RemoteFallback,
                    ModelContextResolver.ResolveRemote(0, modelId, null));
            }
        }
    }
}
