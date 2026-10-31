using System.Net;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;
using UiharuMind.Features.Models;
using UiharuMind.Features.Models.ProviderPicker;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 「更多供应商」：身份条的显隐与文案、选中后表单怎么填、预设家怎么路由、确认时供应商键怎么落盘。
/// 数据用临时快照文件注入，不发真实网络（ADR 0103）。
/// </summary>
public class RemoteModelProviderPickerTests
{
    private const string CacheJson = """
        {
          "schema": 1,
          "fetchedAt": "2026-10-31T12:00:00+08:00",
          "providers": [
            {
              "id": "groq",
              "name": "Groq",
              "api": "https://api.groq.com/openai/v1",
              "doc": "https://console.groq.com",
              "npm": "@ai-sdk/openai-compatible",
              "models": [
                { "id": "llama-3.1-8b-instant", "ctx": 131072, "out": 8192, "cost": [0.05, 0.08, 0, 0] }
              ]
            },
            {
              "id": "deepseek",
              "name": "DeepSeek",
              "api": "https://api.deepseek.com/v1",
              "doc": "https://platform.deepseek.com",
              "npm": "@ai-sdk/openai-compatible",
              "models": []
            }
          ]
        }
        """;

    private sealed class NoopFetcher : IModelCatalogFetcher
    {
        public Task<IReadOnlyList<ModelCatalogEntry>> FetchAsync(string catalogUrl, string? apiKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelCatalogEntry>>([]);
    }

    private sealed class ThrowingFetcher : IModelCatalogFetcher
    {
        public Task<IReadOnlyList<ModelCatalogEntry>> FetchAsync(string catalogUrl, string? apiKey,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Response status code does not indicate success: 401 (Unauthorized).",
                null, HttpStatusCode.Unauthorized);
    }

    private sealed class NoopCache : IModelCatalogCache
    {
        public IReadOnlyList<ModelCatalogEntry>? TryRead(string catalogUrl) => null;

        public void Write(string catalogUrl, IReadOnlyList<ModelCatalogEntry> entries)
        {
        }
    }

    private static async Task<(ModelsDevCatalog Catalog, string Path)> CreateCatalogAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uiharu-provider-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, CacheJson, TestContext.Current.CancellationToken);
        ModelsDevCatalog catalog = new(path);
        await catalog.LoadCacheAsync();
        return (catalog, path);
    }

    private static RemoteModelEditViewData CreateViewModel(ModelsDevCatalog catalog,
        RemoteModelInfo? editing = null) =>
        new(editing, _ => false, new NoopFetcher(), new NoopCache(), catalog);

    /// <summary>预设供应商不出身份条：左侧列表已经高亮，再加一条就是同一件事说两遍</summary>
    [Fact]
    public async Task PresetProvider_HidesStrip()
    {
        (ModelsDevCatalog catalog, string path) = await CreateCatalogAsync();
        try
        {
            RemoteModelEditViewData vm = CreateViewModel(catalog);

            Assert.False(vm.ShowProviderStrip); //默认选中的是 Agnes，预设档
            Assert.True(vm.CanSwitchProvider);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>选中「自定义配置」才出身份条，此时按钮是「更多供应商」</summary>
    [Fact]
    public async Task CustomConfig_ShowsStripWithMoreProviders()
    {
        (ModelsDevCatalog catalog, string path) = await CreateCatalogAsync();
        try
        {
            RemoteModelEditViewData vm = CreateViewModel(catalog);
            vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteModelConfig));

            Assert.True(vm.ShowProviderStrip);
            Assert.False(vm.HasCatalogProvider);
            string beforePick = vm.ProviderSwitchText;

            vm.ApplyCatalogProvider(catalog.Providers.First(p => p.Id == "groq"));

            Assert.True(vm.HasCatalogProvider);
            Assert.Equal("Groq", vm.CatalogProviderName);
            Assert.NotEqual(beforePick, vm.ProviderSwitchText); //「更多供应商」→「换一家」
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>选中一家就把地址、目录、格式都填进表单，并把供应商键写回配置</summary>
    [Fact]
    public async Task ApplyCatalogProvider_FillsFormAndPersistsKey()
    {
        (ModelsDevCatalog catalog, string path) = await CreateCatalogAsync();
        try
        {
            RemoteModelEditViewData vm = CreateViewModel(catalog);
            vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteModelConfig));

            vm.ApplyCatalogProvider(catalog.Providers.First(p => p.Id == "groq"));

            Assert.Equal("https://api.groq.com/openai/v1", vm.ModelPath);
            Assert.Equal("https://api.groq.com/openai/v1/models", vm.CatalogUrlText);
            Assert.True(vm.HasCatalog);

            vm.ModelName = "groq-llama";
            vm.ApiKey = "gsk_x";
            vm.ModelId = "llama-3.1-8b-instant";
            RemoteModelInfo result = vm.BuildResult();

            Assert.Equal("groq", result.Config.CatalogProviderKey);
            Assert.Equal("https://api.groq.com/openai/v1", result.Config.ModelPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>选到已内置的家直接切到那家预设行，不建一条重复的自定义配置</summary>
    [Fact]
    public async Task ApplyCatalogProvider_KnownProvider_SwitchesToPresetRow()
    {
        (ModelsDevCatalog catalog, string path) = await CreateCatalogAsync();
        try
        {
            RemoteModelEditViewData vm = CreateViewModel(catalog);
            vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteModelConfig));

            vm.ApplyCatalogProvider(catalog.Providers.First(p => p.Id == "deepseek"));

            Assert.Equal(typeof(RemoteDeepSeekModelConfig), vm.SelectedProvider?.ConfigType);
            Assert.False(vm.HasCatalogProvider); //没走自定义配置，自然没有身份条
            Assert.False(vm.ShowProviderStrip);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>编辑一个从 models.dev 建的模型：身份条按存下来的键还原，且不给换（左侧列表本来就禁用）</summary>
    [Fact]
    public async Task EditMode_RestoresStripFromStoredKey()
    {
        (ModelsDevCatalog catalog, string path) = await CreateCatalogAsync();
        try
        {
            RemoteModelInfo source = new()
            {
                Config = new RemoteModelConfig
                {
                    ModelName = "groq-llama",
                    ModelId = "llama-3.1-8b-instant",
                    ModelPath = "https://api.groq.com/openai/v1",
                    CatalogProviderKey = "groq",
                },
                ApiKey = "gsk_x",
            };

            RemoteModelEditViewData vm = CreateViewModel(catalog, source);

            Assert.True(vm.ShowProviderStrip);
            Assert.Equal("Groq", vm.CatalogProviderName);
            Assert.False(vm.CanSwitchProvider);
            // 编辑模式不动表单：供应商键原样带回去
            Assert.Equal("groq", vm.BuildResult().Config.CatalogProviderKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>缓存里没有那家（键还在）时也要认出来：至少让用户看得出这个模型属于谁</summary>
    [Fact]
    public async Task EditMode_UnknownKey_FallsBackToRawKey()
    {
        (ModelsDevCatalog catalog, string path) = await CreateCatalogAsync();
        try
        {
            RemoteModelInfo source = new()
            {
                Config = new RemoteModelConfig
                {
                    ModelName = "ghost",
                    ModelId = "m",
                    ModelPath = "https://api.ghost.example/v1",
                    CatalogProviderKey = "ghost",
                },
                ApiKey = "",
            };

            RemoteModelEditViewData vm = CreateViewModel(catalog, source);

            Assert.True(vm.ShowProviderStrip);
            Assert.Equal("ghost", vm.CatalogProviderName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 目录拉取失败要把原因说出来：只说「失败」等于没说——401 要去填密钥、超时要重试，
    /// 处置完全不同。这条钉的是状态码与「先填密钥」的提示都在
    /// </summary>
    [Fact]
    public async Task CatalogFetch_Unauthorized_ShowsStatusAndHint()
    {
        (ModelsDevCatalog catalog, string path) = await CreateCatalogAsync();
        try
        {
            var vm = new RemoteModelEditViewData(null, _ => false, new ThrowingFetcher(), new NoopCache(), catalog);
            vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteModelConfig));
            vm.ApplyCatalogProvider(catalog.Providers.First(p => p.Id == "groq"));

            await vm.RefreshCatalogCommand.ExecuteAsync(null);

            Assert.Contains("401", vm.CatalogFetchError);
            Assert.True(vm.CatalogFetchErrorVisible);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>选择器：搜索按名称与 id 过滤，已内置的家置顶</summary>
    [Fact]
    public async Task Picker_SearchFiltersAndPutsBuiltInFirst()
    {
        (ModelsDevCatalog catalog, string path) = await CreateCatalogAsync();
        try
        {
            ProviderPickerViewData picker = new(_ => Task.CompletedTask, catalog);
            await picker.BeginAsync();

            Assert.Equal(2, picker.Items.Count);
            Assert.Equal("deepseek", picker.Items[0].Provider.Id); //已内置的置顶
            Assert.True(picker.Items[0].IsBuiltIn);
            Assert.False(picker.Items[1].IsBuiltIn);

            picker.SearchText = "gro";
            Assert.Equal("groq", Assert.Single(picker.Items).Provider.Id);

            picker.SearchText = "https://nope";
            Assert.Empty(picker.Items);
            Assert.True(picker.IsEmpty);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
