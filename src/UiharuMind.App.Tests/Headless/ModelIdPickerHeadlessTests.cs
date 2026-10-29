using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 模型 ID 选择器：排序头住<em>浮层里</em>（不是框外那一列），且没有价格可排的供应商不显示它。
/// 钉的是用户实际看到的浮层：输入框右侧的箭头 → 弹出面板顶部那排「目录/输入价/输出价/缓存读价」。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ModelIdPickerHeadlessTests
{
    private sealed class FakeFetcher : IModelCatalogFetcher
    {
        public Task<IReadOnlyList<ModelCatalogEntry>> FetchAsync(string catalogUrl, string? apiKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelCatalogEntry>>([]);
    }

    private sealed class FakeCache : IModelCatalogCache
    {
        public IReadOnlyList<ModelCatalogEntry>? Stored { get; set; }
        public IReadOnlyList<ModelCatalogEntry>? TryRead(string catalogUrl) => Stored;
        public void Write(string catalogUrl, IReadOnlyList<ModelCatalogEntry> entries) => Stored = entries;
    }

    private static (Window Window, RemoteModelEditViewData Vm, Popup Picker) Open(Type providerType,
        params string[] modelIds)
    {
        var cache = new FakeCache
        {
            Stored = modelIds.Select(id => new ModelCatalogEntry
            {
                Id = id,
                Name = id,
                ContextLength = 256000,
                SupportedEndpoints = ["/chat/completions"],
            }).ToList(),
        };
        var vm = new RemoteModelEditViewData(null, _ => false, new FakeFetcher(), cache);
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == providerType);
        var window = new CreateRemoteLlmModelWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        vm.IsModelPickerOpen = true;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, vm, window.FindControl<Popup>("ModelIdPopup")!);
    }

    /// <summary>控件中心在窗口坐标里的位置（逐级累加 Bounds，避开 TranslatePoint 的版本差异）</summary>
    private static Point InWindow(Visual visual, Window window)
    {
        var point = new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2);
        Visual? current = visual;
        while (current is not null && !ReferenceEquals(current, window))
        {
            point = new Point(point.X + current.Bounds.X, point.Y + current.Bounds.Y);
            current = current.GetVisualParent();
        }

        return point;
    }

    private static ListBox? SortHeader(Popup picker) => picker.Child?
        .GetVisualDescendants().OfType<ListBox>()
        .FirstOrDefault(box => box.ItemsSource is ModelSortOption[]);

    /// <summary>公布了单价的供应商：浮层顶部带排序头，列表能按价格重排</summary>
    [Fact]
    public void PricedProvider_ShowsSortHeaderInsidePopup()
    {
        HeadlessUi.Run(() =>
        {
            (Window window, RemoteModelEditViewData vm, Popup picker) = Open(
                typeof(RemoteCommandCodeModelConfig),
                "xiaomi/mimo-v2.6-pro", "deepseek/deepseek-v4.1-flash", "minimax/minimax-m3");

            Assert.True(vm.HasSortablePrices);
            ListBox header = SortHeader(picker)!;
            Assert.NotNull(header);
            Assert.True(header.IsEffectivelyVisible);
            Assert.Equal(4, header.ItemCount);

            vm.SelectedSortOption = vm.SortOptions.First(o => o.Sort == EModelPriceSort.Input);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("deepseek/deepseek-v4.1-flash", vm.VisibleModelIdOptions[0].Id); // $0.15 最便宜
            window.Close();
        });
    }

    /// <summary>一条价都没公布的供应商：浮层照常能选模型，但排序头不出现（否则是个点了没反应的开关）</summary>
    [Fact]
    public void UnpricedProvider_HidesSortHeader()
    {
        HeadlessUi.Run(() =>
        {
            (Window window, RemoteModelEditViewData vm, Popup picker) = Open(
                typeof(RemoteSensenovaModelConfig), "sensenova-5.0-pro", "sensenova-5.0-lite");

            Assert.False(vm.HasSortablePrices);
            Assert.True(vm.ModelIdOptions.Count > 0); // 候选还在，仍能选

            ListBox header = SortHeader(picker)!;
            Assert.NotNull(header);
            Assert.False(header.IsEffectivelyVisible);
            window.Close();
        });
    }

    /// <summary>点候选项：ID 写回、浮层收起（这条替代了原先 ComboBox 的点选路径）</summary>
    [Fact]
    public void ClickingOption_WritesIdAndClosesPicker()
    {
        HeadlessUi.Run(() =>
        {
            (Window window, RemoteModelEditViewData vm, Popup picker) = Open(
                typeof(RemoteCommandCodeModelConfig),
                "deepseek/deepseek-v4.1-flash", "minimax/minimax-m3");

            vm.ModelId = "";
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Button target = picker.Child!.GetVisualDescendants().OfType<Button>()
                .First(b => b.DataContext is ModelIdOptionItem { Id: "minimax/minimax-m3" });
            Point center = InWindow(target, window);

            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("minimax/minimax-m3", vm.ModelId);
            Assert.False(picker.IsOpen);
            window.Close();
        });
    }

    /// <summary>点箭头是开关：连点两次要能收回去（浮层的 light dismiss 与按钮 Click 不能互相打架）</summary>
    [Fact]
    public void ArrowButton_TogglesPopupClosed()
    {
        HeadlessUi.Run(() =>
        {
            (Window window, RemoteModelEditViewData vm, Popup picker) = Open(
                typeof(RemoteCommandCodeModelConfig), "deepseek/deepseek-v4.1-flash");

            vm.IsModelPickerOpen = false;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Button arrow = window.FindControl<Button>("ModelIdDropDownButton")!;
            Point center = InWindow(arrow, window);

            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(picker.IsOpen);

            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(picker.IsOpen);
            window.Close();
        });
    }
}
