using System.Text.Json;
using UiharuMind.Core.Configs;
using Xunit;

namespace UiharuMind.Core.Tests.AI;

public class ModelRuntimeOverridesTests
{
    [Fact]
    public void ForModel_WithoutOverrides_ReturnsGlobal()
    {
        ModelRuntimeSettingConfig config = new() { ContextSize = 8192 };

        Assert.Same(config, config.ForModel("a"));
    }

    [Fact]
    public void ForModel_AppliesOnlySetFields()
    {
        ModelRuntimeSettingConfig config = new() { ContextSize = 8192, GpuLayers = -1, Threads = 4, LocalEngineId = "llamacpp" };
        config.SetOverrides("a", new ModelRuntimeOverrides { ContextSize = 32768, GpuLayers = 0 });

        ModelRuntimeSettingConfig effective = config.ForModel("a");

        Assert.NotSame(config, effective);
        Assert.Equal(32768, effective.ContextSize);
        Assert.Equal(0, effective.GpuLayers);
        Assert.Equal(4, effective.Threads);
        Assert.Equal("llamacpp", effective.LocalEngineId);
        Assert.Equal(8192, config.ForModel("b").ContextSize);
    }

    [Fact]
    public void SetOverrides_Empty_RemovesEntry()
    {
        ModelRuntimeSettingConfig config = new();
        config.SetOverrides("a", new ModelRuntimeOverrides { Threads = 2 });
        config.SetOverrides("a", new ModelRuntimeOverrides());

        Assert.Empty(config.ModelOverrides);
        Assert.Null(config.GetOverrides("a"));
    }

    [Fact]
    public void Overrides_RoundTripThroughJson()
    {
        ModelRuntimeSettingConfig config = new();
        config.SetOverrides("a", new ModelRuntimeOverrides { BatchSize = 1024 });

        string json = JsonSerializer.Serialize(config);
        ModelRuntimeSettingConfig loaded = JsonSerializer.Deserialize<ModelRuntimeSettingConfig>(json)!;

        Assert.DoesNotContain("IsEmpty", json);
        Assert.Equal(1024, loaded.ForModel("a").BatchSize);
    }
}
