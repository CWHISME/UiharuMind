using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 目录拉取失败那行文字必须来自视图模型。曾经这里绑的是静态资源串（只有可见性绑了 VM），
/// 于是后台算出的「401 Unauthorized · 请先填写密钥」被固定文案整个盖掉——用户只看到「拉取失败」。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class CatalogFetchErrorHeadlessTests
{
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

    [Fact]
    public void ErrorText_ComesFromViewModel() => HeadlessUi.Run(() =>
    {
        var vm = new RemoteModelEditViewData(null, _ => false, new NoopFetcher(), new NoopCache());
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteModelConfig));
        vm.CatalogFetchError = "模型目录拉取失败：401 Unauthorized · 请先填写密钥";

        var window = new CreateRemoteLlmModelWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        try
        {
            string[] texts = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(block => block.IsVisible)
                .Select(block => block.Text ?? "")
                .ToArray();

            Assert.Contains(texts, text => text.Contains("401", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
        }
    });
}
