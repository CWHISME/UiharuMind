using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 「更多供应商」身份条：真控件、真模板，钉三件事——
/// 预设档不出条、自定义档出条、选中一家后条上的名字跟着变。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ProviderStripHeadlessTests
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
              "doc": "",
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

    private sealed class NoopCache : IModelCatalogCache
    {
        public IReadOnlyList<ModelCatalogEntry>? TryRead(string catalogUrl) => null;

        public void Write(string catalogUrl, IReadOnlyList<ModelCatalogEntry> entries)
        {
        }
    }

    private static async Task<ModelsDevCatalog> CreateCatalogAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uiharu-strip-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, CacheJson, TestContext.Current.CancellationToken);
        ModelsDevCatalog catalog = new(path);
        await catalog.LoadCacheAsync();
        return catalog;
    }

    /// <summary>身份条是右栏第一行：自定义档看得见，预设档整个折叠掉</summary>
    [Fact]
    public void Strip_VisibleOnlyOnCustomConfig() => HeadlessUi.RunAsync(async () =>
    {
        ModelsDevCatalog catalog = await CreateCatalogAsync();
        var vm = new RemoteModelEditViewData(null, _ => false, new NoopFetcher(), new NoopCache(), catalog);
        var window = new CreateRemoteLlmModelWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        try
        {
            Border strip = window.FindControl<Border>("ProviderStrip")!;
            Assert.False(strip.IsVisible); //默认选中的是预设供应商

            vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteModelConfig));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(strip.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>选中一家之后，条上要出现那家的名字</summary>
    [Fact]
    public void Strip_ShowsPickedProviderName() => HeadlessUi.RunAsync(async () =>
    {
        ModelsDevCatalog catalog = await CreateCatalogAsync();
        var vm = new RemoteModelEditViewData(null, _ => false, new NoopFetcher(), new NoopCache(), catalog);
        var window = new CreateRemoteLlmModelWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        try
        {
            vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteModelConfig));
            vm.ApplyCatalogProvider(catalog.Providers[0]);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            string[] texts = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(block => block.IsVisible)
                .Select(block => block.Text ?? "")
                .ToArray();

            Assert.Contains("Groq", texts);
        }
        finally
        {
            window.Close();
        }
    });
}
