using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 添加模型页的「接口地址」候选：地址就是格式，选哪个地址就是哪条线。
/// 默认按模型数据给——Claude 固定 /messages，声明了 /responses 的优先 /responses，其余 chat。
/// </summary>
public class RemoteModelEndpointTests
{
    private sealed class FakeCatalogFetcher : IModelCatalogFetcher
    {
        public Func<string, string?, Task<IReadOnlyList<ModelCatalogEntry>>>? Handler { get; set; }

        public Task<IReadOnlyList<ModelCatalogEntry>> FetchAsync(string catalogUrl, string? apiKey,
            CancellationToken cancellationToken = default) =>
            Handler?.Invoke(catalogUrl, apiKey) ?? Task.FromResult<IReadOnlyList<ModelCatalogEntry>>([]);
    }

    private sealed class FakeCatalogCache : IModelCatalogCache
    {
        public IReadOnlyList<ModelCatalogEntry>? Stored { get; set; }

        public IReadOnlyList<ModelCatalogEntry>? TryRead(string catalogUrl) => Stored;

        public void Write(string catalogUrl, IReadOnlyList<ModelCatalogEntry> entries) => Stored = entries;
    }

    private static ModelCatalogEntry Entry(string id, params string[] endpoints) => new()
    {
        Id = id,
        ContextLength = 200000,
        SupportedEndpoints = endpoints.ToList(),
    };

    private static RemoteModelEditViewData CreateWithCatalog(Type providerType, params ModelCatalogEntry[] entries)
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>(entries),
        };
        var vm = new RemoteModelEditViewData(null, _ => false, fetcher, new FakeCatalogCache());
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == providerType);
        return vm;
    }

    private static RemoteModelEditViewData CreateWithCommandCode(params ModelCatalogEntry[] entries) =>
        CreateWithCatalog(typeof(RemoteCommandCodeModelConfig), entries);

    /// <summary>自定义服务商：地址空着就没有候选（没有 base 可拼）；填成完整地址后补出三条</summary>
    [Fact]
    public void CustomProvider_NoAddress_NoCandidatesUntilOneIsTyped()
    {
        var vm = new RemoteModelEditViewData();
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteModelConfig));
        Assert.Empty(vm.EndpointOptions);

        vm.ModelPath = "https://proxy.example/v1/chat/completions";

        Assert.Equal(
            ["https://proxy.example/v1/chat/completions",
             "https://proxy.example/v1/responses",
             "https://proxy.example/v1/messages"],
            vm.EndpointOptions);
    }

    /// <summary>单线预设（DeepSeek）：只列它自己那条，别摆出两个必 404 的选项</summary>
    [Fact]
    public void PresetWithoutCatalog_ListsOnlyItsOwnLine()
    {
        var vm = new RemoteModelEditViewData();
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteDeepSeekModelConfig));

        Assert.Equal(["https://api.deepseek.com/chat/completions"], vm.EndpointOptions);
    }

    /// <summary>
    /// 目录明说这个 Claude 只走 chat 时听目录的：按 ID 判成 messages 就是稳定 404
    /// （目录是实际端点的声明，比「claude 这个词」可信）
    /// </summary>
    [Fact]
    public void ClaudeWithChatOnlyDeclaration_StaysOnChat()
    {
        RemoteModelEditViewData vm = CreateWithCommandCode(Entry("claude-legacy", "/chat/completions"));

        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "claude-legacy");

        Assert.Equal("https://api.commandcode.ai/provider/v1/chat/completions", vm.ModelPath);
        Assert.Equal(["https://api.commandcode.ai/provider/v1/chat/completions"], vm.EndpointOptions);
        Assert.Equal(EApiFormat.ChatCompletions, vm.BuildResult().Config.ApiFormat);
    }

    /// <summary>
    /// CommandCode 里选 Claude：地址自动换成 <c>.../v1/messages</c>，候选只剩这一条，
    /// 落盘的格式从地址尾段反推（不再另存一个可能打架的字段）
    /// </summary>
    [Fact]
    public void CommandCodeClaude_AddressBecomesMessages()
    {
        RemoteModelEditViewData vm = CreateWithCommandCode(
            Entry("claude-sonnet-5-5", "/messages"),
            Entry("gpt-6-astra", "/chat/completions"));

        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "claude-sonnet-5-5");

        Assert.Equal("https://api.commandcode.ai/provider/v1/messages", vm.ModelPath);
        Assert.Equal(["https://api.commandcode.ai/provider/v1/messages"], vm.EndpointOptions);
        Assert.Equal(EApiFormat.Messages, vm.BuildResult().Config.ApiFormat);
    }

    /// <summary>只声明 /responses 的模型：走 responses 线</summary>
    [Fact]
    public void CatalogPick_ResponsesOnly_SwitchesToResponses()
    {
        RemoteModelEditViewData vm = CreateWithCommandCode(Entry("gpt-6-astra", "/responses"));

        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "gpt-6-astra");

        Assert.Equal("https://api.commandcode.ai/provider/v1/responses", vm.ModelPath);
        Assert.Equal(EApiFormat.Responses, vm.BuildResult().Config.ApiFormat);
    }

    /// <summary>
    /// 两条都声明时留在 chat：chat 是全仓基线（响应清洗、思考票据、缓存审计都建在它上面），
    /// 声明里有 chat 就没理由换到验证面更窄的 responses 线
    /// </summary>
    [Fact]
    public void CatalogPick_BothDeclared_StaysOnChat()
    {
        RemoteModelEditViewData vm = CreateWithCommandCode(Entry("gpt-6-astra", "/chat/completions", "/responses"));

        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "gpt-6-astra");

        Assert.Equal("https://api.commandcode.ai/provider/v1/chat/completions", vm.ModelPath);
        Assert.Equal(EApiFormat.ChatCompletions, vm.BuildResult().Config.ApiFormat);
    }

    /// <summary>目录只声明 chat 的模型：地址留在 chat，候选也只有这一条</summary>
    [Fact]
    public void CatalogPick_ChatOnly_StaysOnChat()
    {
        RemoteModelEditViewData vm = CreateWithCommandCode(Entry("deepseek-v4-flash", "/chat/completions"));

        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "deepseek-v4-flash");

        Assert.Equal("https://api.commandcode.ai/provider/v1/chat/completions", vm.ModelPath);
        Assert.Equal(EApiFormat.ChatCompletions, vm.BuildResult().Config.ApiFormat);
    }

    /// <summary>
    /// OpenCode 的目录是最简形状（只有 id，不声明端点），但它的三条线前缀本来就不同
    /// （chat/responses 在 <c>/inference/openai/v1/</c>，Anthropic 在 <c>/inference/anthropic/v1/</c>）——
    /// 端点由供应商声明，不能靠换尾段拼
    /// </summary>
    [Fact]
    public void OpenCodeClaude_MinimalCatalog_AddressBecomesMessages()
    {
        RemoteModelEditViewData vm = CreateWithCatalog(typeof(RemoteOpenCodeModelConfig),
            new ModelCatalogEntry { Id = "claude-fable-5" });
        Assert.Equal("https://opencode.ai/inference/openai/v1/chat/completions", vm.ModelPath);

        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "claude-fable-5");

        Assert.Equal("https://opencode.ai/inference/anthropic/v1/messages", vm.ModelPath);
        Assert.Equal(EApiFormat.Messages, vm.BuildResult().Config.ApiFormat);
    }

    /// <summary>目录没声明端点的非 Claude 模型：默认 chat，三条地址按供应商声明列出来</summary>
    [Fact]
    public void MinimalCatalog_NonClaude_ListsAllThreeAndDefaultsToChat()
    {
        RemoteModelEditViewData vm = CreateWithCatalog(typeof(RemoteOpenCodeModelConfig),
            new ModelCatalogEntry { Id = "beam-free" });

        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "beam-free");

        Assert.Equal(
            ["https://opencode.ai/inference/openai/v1/chat/completions",
             "https://opencode.ai/inference/openai/v1/responses",
             "https://opencode.ai/inference/anthropic/v1/messages"],
            vm.EndpointOptions);
        Assert.Equal("https://opencode.ai/inference/openai/v1/chat/completions", vm.ModelPath);
    }

    /// <summary>用户把地址改成别的端点：格式跟着地址走（地址是唯一事实）</summary>
    [Fact]
    public void ManualAddress_DrivesApiFormat()
    {
        RemoteModelEditViewData vm = CreateWithCommandCode(Entry("gpt-6-astra", "/chat/completions"));
        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "gpt-6-astra");

        vm.ModelPath = "https://proxy.example/v1/messages";

        Assert.Equal(EApiFormat.Messages, vm.BuildResult().Config.ApiFormat);
    }

    /// <summary>手改过地址之后，再改模型 ID 也不会把地址顶回去（否则「手输即可」的逃生口形同虚设）</summary>
    [Fact]
    public void ManualAddress_SurvivesModelIdEditing()
    {
        RemoteModelEditViewData vm = CreateWithCommandCode(Entry("claude-sonnet-5-5", "/messages"));
        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "claude-sonnet-5-5");
        vm.ModelPath = "https://proxy.example/v1/chat/completions";

        vm.ModelId = "claude-sonnet-5-5-x";

        Assert.Equal("https://proxy.example/v1/chat/completions", vm.ModelPath);
    }

    /// <summary>
    /// 从候选里重新点一次同一项：视为「按这个模型来」——换的是那条线，用户填的网关（host/前缀）保留
    /// </summary>
    [Fact]
    public void RepickingSameModel_ReappliesRecommendationOnUsersOwnHost()
    {
        RemoteModelEditViewData vm = CreateWithCommandCode(Entry("claude-sonnet-5-5", "/messages"));
        ModelIdOptionItem option = vm.ModelIdOptions.First(o => o.Id == "claude-sonnet-5-5");
        vm.SelectModelIdOption(option);
        vm.ModelPath = "https://proxy.example/v1/chat/completions";

        vm.SelectModelIdOption(option);

        Assert.Equal("https://proxy.example/v1/messages", vm.ModelPath);
        Assert.Equal(EApiFormat.Messages, vm.BuildResult().Config.ApiFormat);
    }

    /// <summary>
    /// 地址认不出尾段（只填到 <c>/v1</c>）：保留配置里原来的格式，不擅自翻成 chat。
    /// 运行时按格式补后缀，这种地址本来就合法，判成 chat 等于把一个能跑的模型存坏
    /// </summary>
    [Fact]
    public void BareBaseAddress_KeepsSavedFormat()
    {
        var source = new RemoteModelInfo
        {
            Config = new RemoteModelConfig
            {
                ModelName = "claude-via-proxy",
                ModelId = "claude-sonnet-5-5",
                ModelPath = "https://proxy.example/v1",
                ApiFormat = EApiFormat.Messages,
            },
            ApiKey = "key",
        };

        var vm = new RemoteModelEditViewData(source, _ => false);
        vm.ModelName = "claude-via-proxy";

        Assert.Equal(EApiFormat.Messages, vm.BuildResult().Config.ApiFormat);
    }

    /// <summary>编辑模式：已存的地址原样带回，目录推荐不覆盖它</summary>
    [Fact]
    public void EditMode_KeepsSavedAddress()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>(
                [Entry("claude-sonnet-5-5", "/messages")]),
        };
        var source = new RemoteModelInfo
        {
            Config = new RemoteCommandCodeModelConfig
            {
                ModelName = "saved",
                ModelId = "claude-sonnet-5-5",
                ModelPath = "https://api.commandcode.ai/provider/v1/chat/completions",
            },
            ApiKey = "key",
        };

        var vm = new RemoteModelEditViewData(source, _ => false, fetcher, new FakeCatalogCache());

        Assert.Equal("https://api.commandcode.ai/provider/v1/chat/completions", vm.ModelPath);
        Assert.Equal(EApiFormat.ChatCompletions, vm.BuildResult().Config.ApiFormat);
    }

    /// <summary>复制已有模型：地址一起带过来，格式自然跟着地址</summary>
    [Fact]
    public void CopySource_KeepsAddress()
    {
        var source = new RemoteModelInfo
        {
            Config = new RemoteCommandCodeModelConfig
            {
                ModelName = "claude",
                ModelId = "claude-sonnet-5-5",
                ModelPath = "https://api.commandcode.ai/provider/v1/messages",
            },
            ApiKey = "key",
        };
        var vm = new RemoteModelEditViewData();
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));

        vm.SelectCopySourceCommand.Execute(source);

        Assert.Equal("https://api.commandcode.ai/provider/v1/messages", vm.ModelPath);
        Assert.Equal(EApiFormat.Messages, vm.BuildResult().Config.ApiFormat);
    }

    /// <summary>换服务商＝换地址：新服务商的默认地址，候选按它的 base 重算</summary>
    [Fact]
    public void ProviderSwitch_ResetsAddressToProviderDefault()
    {
        RemoteModelEditViewData vm = CreateWithCommandCode(Entry("gpt-6-astra", "/chat/completions"));
        vm.ModelPath = "https://proxy.example/v1/messages";

        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteDeepSeekModelConfig));

        Assert.Equal("https://api.deepseek.com/chat/completions", vm.ModelPath);
        Assert.Equal(EApiFormat.ChatCompletions, vm.BuildResult().Config.ApiFormat);
    }
}
