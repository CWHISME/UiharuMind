using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 编辑窗口对「运行时模型目录」的消费（ADR 0092）：
/// 候选来自拉取/缓存而非写死变体表；缓存优先、按钮才刷新；/messages-only 过滤；输入过滤；ApiFormat 落盘。
/// 全部走注入的 fake，不发真实网络。
/// </summary>
public class RemoteModelEditCatalogTests
{
    private const string CatalogUrl = "https://api.commandcode.ai/provider/v1/models";

    private sealed class FakeCatalogFetcher : IModelCatalogFetcher
    {
        public Func<string, string?, Task<IReadOnlyList<ModelCatalogEntry>>>? Handler { get; set; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ModelCatalogEntry>> FetchAsync(string catalogUrl, string? apiKey,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Handler?.Invoke(catalogUrl, apiKey) ??
                   Task.FromResult<IReadOnlyList<ModelCatalogEntry>>([]);
        }
    }

    private sealed class FakeCatalogCache : IModelCatalogCache
    {
        public IReadOnlyList<ModelCatalogEntry>? Stored { get; set; }
        public string? LastWrittenUrl { get; private set; }
        public int WriteCalls { get; private set; }

        public IReadOnlyList<ModelCatalogEntry>? TryRead(string catalogUrl) => Stored;

        public void Write(string catalogUrl, IReadOnlyList<ModelCatalogEntry> entries)
        {
            LastWrittenUrl = catalogUrl;
            Stored = entries;
            WriteCalls++;
        }
    }

    private static ModelCatalogEntry Entry(string id, params string[] endpoints) => new()
    {
        Id = id,
        Name = id.Replace("/", " ") + " Name",
        ContextLength = 256000,
        SupportedEndpoints = endpoints.ToList(),
    };

    private static RemoteModelEditViewData CreateVm(FakeCatalogFetcher fetcher, FakeCatalogCache cache) =>
        new(null, _ => false, fetcher, cache);

    /// <summary>切到 CommandCode：拉取目录 → 候选填入；/messages-only 的 Claude 被过滤；缓存被写入</summary>
    [Fact]
    public void ProviderSwitch_CommandCode_FillsOptionsAndFiltersMessagesOnly()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (url, key) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>(
            [
                Entry("claude-sonnet-5-5", "/messages"),
                Entry("gpt-6-astra", "/chat/completions", "/responses"),
                Entry("deepseek/deepseek-v4-flash", "/chat/completions"),
            ]),
        };
        var cache = new FakeCatalogCache();
        var vm = CreateVm(fetcher, cache);

        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));

        Assert.Equal(1, fetcher.Calls);
        Assert.Equal(CatalogUrl, cache.LastWrittenUrl);
        Assert.True(vm.HasModelIdOptions);
        Assert.Equal(["gpt-6-astra", "deepseek/deepseek-v4-flash"], vm.ModelIdOptions.Select(o => o.Id));
        Assert.DoesNotContain(vm.ModelIdOptions, o => o.Id == "claude-sonnet-5-5");
        Assert.Equal(EApiFormat.ChatCompletions, vm.ModelIdOptions[0].ApiFormat);
    }

    /// <summary>端点字段缺失（OpenRouter 等标准 /v1/models 不返回）：默认展示，不当 /messages-only 过滤（Spec 评审 c-1）</summary>
    [Fact]
    public void Catalog_EntryWithoutEndpoints_IsShownByDefault()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>(
                [Entry("openrouter-model")]),
        };
        var vm = CreateVm(fetcher, new FakeCatalogCache());

        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));

        Assert.Equal("openrouter-model", Assert.Single(vm.ModelIdOptions).Id);
    }

    /// <summary>OpenCode 变体：目录最简形状（仅 id，实测 2026-10-26）也能走通通用流程</summary>
    [Fact]
    public void ProviderSwitch_OpenCode_FillsOptionsForMinimalCatalogShape()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>(
                [new ModelCatalogEntry { Id = "beam-free" }, new ModelCatalogEntry { Id = "claude-fable-5" }]),
        };
        var cache = new FakeCatalogCache();
        var vm = CreateVm(fetcher, cache);

        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteOpenCodeModelConfig));

        Assert.True(vm.HasModelIdOptions);
        Assert.Equal(["beam-free", "claude-fable-5"], vm.ModelIdOptions.Select(o => o.Id));
        Assert.Equal(1, fetcher.Calls);
        Assert.Equal("https://opencode.ai/inference/v1/models", cache.LastWrittenUrl);
    }

    /// <summary>有本地缓存时切服务商：直接展示缓存，不碰网络（Q12 修订：有缓存不自动拉）</summary>
    [Fact]
    public void ProviderSwitch_WithCache_SkipsNetworkFetch()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => throw new InvalidOperationException("不应发网络请求"),
        };
        var cache = new FakeCatalogCache { Stored = [Entry("gpt-6-astra", "/chat/completions")] };
        var vm = CreateVm(fetcher, cache);

        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));

        Assert.Equal(0, fetcher.Calls);
        Assert.Equal("gpt-6-astra", Assert.Single(vm.ModelIdOptions).Id);
    }

    /// <summary>空列表缓存也是缓存（服务商合法返回 data:[]）：有缓存就不拉网络（Spec 评审 c-2）</summary>
    [Fact]
    public void ProviderSwitch_WithEmptyCache_SkipsNetworkFetch()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => throw new InvalidOperationException("不应发网络请求"),
        };
        var cache = new FakeCatalogCache { Stored = [] };
        var vm = CreateVm(fetcher, cache);

        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));

        Assert.Equal(0, fetcher.Calls);
        Assert.False(vm.HasModelIdOptions);
    }

    /// <summary>无缓存时自动拉一次并写入缓存</summary>
    [Fact]
    public void ProviderSwitch_NoCache_AutoFetchesAndWritesCache()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (url, key) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>([Entry("gpt-6-astra", "/chat/completions")]),
        };
        var cache = new FakeCatalogCache();
        var vm = CreateVm(fetcher, cache);

        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));

        Assert.Equal(1, fetcher.Calls);
        Assert.Equal(1, cache.WriteCalls);
        Assert.Equal(CatalogUrl, cache.LastWrittenUrl);
    }

    /// <summary>无缓存且拉取失败：候选为空 + 报错；仍可手输（HasModelIdOptions=false 走 TextBox）</summary>
    [Fact]
    public void FetchFailure_NoCache_EmptyOptionsAndError()
    {
        var fetcher = new FakeCatalogFetcher { Handler = (_, _) => throw new HttpRequestException("down") };
        var cache = new FakeCatalogCache();
        var vm = CreateVm(fetcher, cache);

        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));

        Assert.False(vm.HasModelIdOptions);
        Assert.False(string.IsNullOrEmpty(vm.CatalogFetchError));
    }

    /// <summary>手动「拉取」：有缓存也强制拉，成功覆盖缓存与列表</summary>
    [Fact]
    public void ManualRefresh_AlwaysFetches_OverwritesCache()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>([Entry("grok-4.6", "/chat/completions")]),
        };
        var cache = new FakeCatalogCache { Stored = [Entry("gpt-6-astra", "/chat/completions")] };
        var vm = CreateVm(fetcher, cache);
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));

        vm.RefreshCatalogCommand.Execute(null);

        Assert.Equal(1, fetcher.Calls);
        Assert.Equal("grok-4.6", Assert.Single(vm.ModelIdOptions).Id);
        Assert.Equal("grok-4.6", Assert.Single(cache.Stored!).Id);
    }

    /// <summary>手动「拉取」失败：保留缓存展示并提示（不把列表清空）</summary>
    [Fact]
    public void ManualRefresh_Failure_KeepsCacheAndShowsError()
    {
        var fetcher = new FakeCatalogFetcher { Handler = (_, _) => throw new HttpRequestException("down") };
        var cache = new FakeCatalogCache { Stored = [Entry("gpt-6-astra", "/chat/completions")] };
        var vm = CreateVm(fetcher, cache);
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));

        vm.RefreshCatalogCommand.Execute(null);

        Assert.Equal(1, fetcher.Calls);
        Assert.Equal("gpt-6-astra", Assert.Single(vm.ModelIdOptions).Id); // 缓存仍在
        Assert.False(string.IsNullOrEmpty(vm.CatalogFetchError));
    }

    /// <summary>下拉选目录项：模型名跟着显示名自动变（未手改或为空时）</summary>
    [Fact]
    public void CatalogPick_PrefillsModelNameFromDisplayName()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>(
                [new ModelCatalogEntry { Id = "grok-4.6", Name = "Grok 4.6", ContextLength = 500000, SupportedEndpoints = ["/chat/completions"] }]),
        };
        var vm = CreateVm(fetcher, new FakeCatalogCache());
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));

        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "grok-4.6");

        Assert.Equal("Grok 4.6", vm.ModelName);
    }

    /// <summary>用户手改过模型名：选目录项不再覆盖</summary>
    [Fact]
    public void CatalogPick_AfterUserEditedName_KeepsUserValue()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>(
                [new ModelCatalogEntry { Id = "grok-4.6", Name = "Grok 4.6", ContextLength = 500000, SupportedEndpoints = ["/chat/completions"] }]),
        };
        var vm = CreateVm(fetcher, new FakeCatalogCache());
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));
        vm.ModelName = "我的模型";

        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "grok-4.6");

        Assert.Equal("我的模型", vm.ModelName);
    }

    /// <summary>编辑模式带已存名字：换目录项不覆盖已存名</summary>
    [Fact]
    public void EditMode_PickAnotherCatalogModel_KeepsSavedName()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>(
            [
                new ModelCatalogEntry { Id = "grok-4.6", Name = "Grok 4.6", ContextLength = 500000, SupportedEndpoints = ["/chat/completions"] },
                new ModelCatalogEntry { Id = "gpt-5.5", Name = "GPT-5.5", ContextLength = 400000, SupportedEndpoints = ["/chat/completions"] },
            ]),
        };
        var cache = new FakeCatalogCache();
        var source = new RemoteModelInfo
        {
            Config = new RemoteCommandCodeModelConfig { ModelName = "saved-name", ModelId = "grok-4.6" },
            ApiKey = "key",
        };
        var vm = new RemoteModelEditViewData(source, _ => false, fetcher, cache);

        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "gpt-5.5");

        Assert.Equal("saved-name", vm.ModelName);
    }

    /// <summary>预设（无目录、Alias 空）：选变体时名字跟随模型 ID（没有显示名可跟）</summary>
    [Fact]
    public void PresetPick_SyncsName_WithModelId()
    {
        var vm = new RemoteModelEditViewData();
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteDeepSeekModelConfig));
        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "deepseek-v4-pro");

        Assert.Equal("deepseek-v4-pro", vm.ModelName);
    }

    /// <summary>下拉选择目录项：上下文长度按目录预填（ApplyModelIdPreset 同一条路）</summary>
    [Fact]
    public void DropdownSelect_CatalogItem_PrefillsContextLength()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>(
                [new ModelCatalogEntry { Id = "gpt-6-astra", Name = "GPT-6 Astra", ContextLength = 1050000, SupportedEndpoints = ["/chat/completions"] }]),
        };
        var vm = CreateVm(fetcher, new FakeCatalogCache());
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));

        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "gpt-6-astra");

        Assert.Equal("gpt-6-astra", vm.ModelId);
        Assert.Equal("1050000", vm.ContextLengthText);
    }

    /// <summary>输入过滤：ModelId 文本按 id/显示名过滤 VisibleModelIdOptions，空文本恢复全量</summary>
    [Fact]
    public void ModelIdText_FiltersVisibleOptions()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>(
            [
                Entry("grok-4.5", "/chat/completions"),
                Entry("xai/grok-4.6", "/chat/completions"),
                Entry("gpt-5.5", "/chat/completions"),
            ]),
        };
        var vm = CreateVm(fetcher, new FakeCatalogCache());
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));

        vm.ModelId = "grok";

        Assert.Equal(["grok-4.5", "xai/grok-4.6"], vm.VisibleModelIdOptions.Select(o => o.Id));

        vm.ModelId = "";
        Assert.Equal(3, vm.VisibleModelIdOptions.Count);
    }

    /// <summary>选中目录项后确认：ApiFormat 按目录端点写入配置（默认 ChatCompletions）</summary>
    [Fact]
    public void BuildResult_WritesApiFormat_FromSelectedCatalogItem()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>(
                [Entry("gpt-6-astra", "/chat/completions", "/responses")]),
        };
        var vm = CreateVm(fetcher, new FakeCatalogCache());
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteCommandCodeModelConfig));
        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "gpt-6-astra");
        vm.ModelName = "gpt-test";

        RemoteModelInfo result = vm.BuildResult();

        Assert.Equal(EApiFormat.ChatCompletions, result.Config.ApiFormat);
    }

    /// <summary>自定义服务商：填目录地址字段才拉取，地址透传给 fetcher</summary>
    [Fact]
    public void CustomProvider_CatalogUrlText_TriggersFetchWithThatUrl()
    {
        var fetcher = new FakeCatalogFetcher { Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>([Entry("custom-1", "/chat/completions")]) };
        var vm = CreateVm(fetcher, new FakeCatalogCache());
        string? requestedUrl = null;
        fetcher.Handler = (url, key) =>
        {
            requestedUrl = url;
            return Task.FromResult<IReadOnlyList<ModelCatalogEntry>>([Entry("custom-1", "/chat/completions")]);
        };

        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteModelConfig));
        vm.CatalogUrlText = "https://custom.example/v1/models";

        Assert.Equal(1, fetcher.Calls);
        Assert.Equal("https://custom.example/v1/models", requestedUrl);
        Assert.Equal("custom-1", Assert.Single(vm.ModelIdOptions).Id);
    }

    /// <summary>编辑模式初始化（CommandCode 已存档）：拉目录但不覆盖已存 ModelId，选中项对齐</summary>
    [Fact]
    public void EditMode_Init_LoadsCatalog_PreservesSavedModelId()
    {
        var fetcher = new FakeCatalogFetcher
        {
            Handler = (_, _) => Task.FromResult<IReadOnlyList<ModelCatalogEntry>>(
                [Entry("grok-4.6", "/chat/completions"), Entry("gpt-5.5", "/chat/completions")]),
        };
        var cache = new FakeCatalogCache();
        var source = new RemoteModelInfo
        {
            Config = new RemoteCommandCodeModelConfig
            {
                ModelName = "saved",
                ModelId = "grok-4.6",
                ContextLength = 32768,
            },
            ApiKey = "key",
        };
        var vm = new RemoteModelEditViewData(source, _ => false, fetcher, cache);

        Assert.Equal("grok-4.6", vm.ModelId); // 不被覆盖
        Assert.Equal("grok-4.6", vm.SelectedModelIdOption?.Id); // 选中项对齐到目录里的同一条
        Assert.Equal("32768", vm.ContextLengthText); // 用户手动值不被目录预填覆盖
    }
}
