using UiharuMind.Core.AI.Core;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 本地引擎可插拔：用哪个是配置，不是写死。
/// 旧档案里存着已屏蔽引擎的 Id（如 LLamaSharp），必须落到还能跑的那个，而不是报「没有引擎」。
/// </summary>
public class ModelRuntimeBackendRegistryTests
{
    private readonly ModelRuntimeBackendRegistry _registry = new();
    private readonly GGufModelInfo _localModel = new() { ModelName = "local" };
    private readonly EmbeddingModelSettingConfig _localEmbedding = new();

    public ModelRuntimeBackendRegistryTests()
    {
        _registry.Register(new FakeBackend("Remote", isLocal: false));
        _registry.Register(new FakeBackend("EngineA", isLocal: true));
        _registry.Register(new FakeBackend("EngineB", isLocal: true));
    }

    [Fact]
    public void PreferredEngine_WinsWhenItCanRun()
    {
        Assert.Equal("EngineB", _registry.FindChatBackend(_localModel, "EngineB")?.Id);
        Assert.Equal("EngineB", _registry.FindEmbeddingBackend(_localEmbedding, "EngineB")?.Id);
    }

    [Theory]
    [InlineData("LLamaSharp")] //已屏蔽、未注册
    [InlineData("")]
    [InlineData(null)]
    public void UnknownOrUnsetEngine_FallsBackToFirstCapable(string? preferred)
    {
        Assert.Equal("EngineA", _registry.FindChatBackend(_localModel, preferred)?.Id);
        Assert.Equal("EngineA", _registry.FindEmbeddingBackend(_localEmbedding, preferred)?.Id);
    }

    [Fact]
    public void LocalEngines_ExcludeRemote()
    {
        Assert.Equal(["EngineA", "EngineB"], _registry.LocalEngines.Select(x => x.Id));
    }

    [Fact]
    public void ToolCalling_FollowsTheEngine_NotWhetherTheModelIsRemote()
    {
        ModelRunningData local = new(_localModel);

        local.ApplyBackend(new FakeBackend("WithTools", true, RuntimeCapability.ToolCalling));
        Assert.True(local.SupportsToolCalling);

        local.ApplyBackend(new FakeBackend("NoTools", true));
        Assert.False(local.SupportsToolCalling);

        local.ApplyBackend(null);
        Assert.False(local.SupportsToolCalling);
    }

    private sealed class FakeBackend(string id, bool isLocal, params RuntimeCapability[] capabilities)
        : IModelRuntimeBackend
    {
        public string Id => id;
        public string DisplayName => id;
        public IReadOnlySet<RuntimeCapability> Capabilities { get; } = new HashSet<RuntimeCapability>(capabilities);
        public bool IsLocal => isLocal;

        public bool CanHandleChat(ILlmModel model) => isLocal == model is GGufModelInfo;

        public bool CanHandleEmbedding(EmbeddingModelSettingConfig settings) =>
            isLocal != EmbeddingModelResolver.IsRemote(settings);

        public RuntimeParameterPolicy CreateParameterPolicy(ModelRuntimeSettingConfig settings) =>
            new(RuntimeDeviceMode.Cpu, false, true);

        public Task<IReadOnlyDictionary<string, ILlmModel>> DiscoverModelsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, ILlmModel>>(new Dictionary<string, ILlmModel>());

        public Task RunChatAsync(ModelRuntimeRequest request, Action<float>? onLoading,
            Action<IChatClient>? onLoadOver, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IEmbeddingSession> CreateEmbeddingSessionAsync(EmbeddingRuntimeRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
