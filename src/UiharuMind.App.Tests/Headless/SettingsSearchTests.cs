using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Features.About;
using UiharuMind.Features.Settings;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 设置窗口的全局搜索：索引要从「页面声明的 XAML 树」里收行，
/// 不能只看已渲染的可视树——否则没被打开过的页、TabControl 里没选中的 Tab，
/// 里面的设置项一条都搜不到（用户在搜索框里搜的就是「我没看见的那个开关在哪」）。
///
/// 组装与截图测试同一套注入路径（无头 App 下 <c>App.ViewModel</c> 为 null）。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SettingsSearchTests : IDisposable
{
    private SettingsWindow? _window;

    public void Dispose() => HeadlessUi.Run(() => _window?.Close());

    [Fact]
    public void Search_FindsRowsOnOtherPageAndInsideUnopenedTab()
    {
        HeadlessUi.Run(() =>
        {
            (SettingsWindow window, TextBox searchBox, ListBox resultList, Border resultPanel) = NewWindow();

            // 当前页里的行
            SetKeyword(searchBox, Loc.Text(LangKey.ThemeSetting));
            Assert.True(resultPanel.IsVisible);
            Assert.Contains(SearchHits(resultList),
                hit => hit.PageTitleKey == "GeneralSetting" && hit.Row.Header == Loc.Text(LangKey.ThemeSetting));

            // 从没打开过的页（Shortcut 页）里的行也要在索引里：搜页名应整页列出
            SetKeyword(searchBox, Loc.Text(LangKey.ShortcutsSetting));
            Assert.Contains(SearchHits(resultList), hit => hit.PageTitleKey == "ShortcutsSetting");

            // TODO(白露迁完 Agent): 补「TabControl 未选中 Tab 里的行」断言，
            // 现在 Agent 页还没用 SettingsRow，索引里自然没有它
        });
    }

    [Fact]
    public void Search_EmptyKeyword_KeepsPanelHidden()
    {
        HeadlessUi.Run(() =>
        {
            (_, TextBox searchBox, _, Border resultPanel) = NewWindow();

            SetKeyword(searchBox, Loc.Text(LangKey.ThemeSetting));
            Assert.True(resultPanel.IsVisible);

            SetKeyword(searchBox, "   ");
            Assert.False(resultPanel.IsVisible);
        });
    }

    [Fact]
    public void SearchResult_Pick_SwitchesToThatPage()
    {
        HeadlessUi.Run(() =>
        {
            (SettingsWindow window, TextBox searchBox, ListBox resultList, _) = NewWindow();
            ContentControl content = window.FindControl<ContentControl>("SettingsContent")!;
            Assert.IsType<GeneralSettingView>(content.Content);

            SetKeyword(searchBox, Loc.Text(LangKey.ShortcutCaptureScreen));
            SettingsSearchHit hit = SearchHits(resultList)
                .Single(h => h.PageTitleKey == "ShortcutsSetting");

            resultList.SelectedItem = hit;
            Dispatcher.UIThread.RunJobs();

            Assert.IsType<ShortcutSettingView>(content.Content);
        });
    }

    private (SettingsWindow Window, TextBox SearchBox, ListBox ResultList, Border ResultPanel) NewWindow()
    {
        SettingsWindow window = BuildWindow();
        _window = window;
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return (window,
            window.FindControl<TextBox>("SearchBox") ?? throw new InvalidOperationException("没有搜索框"),
            window.FindControl<ListBox>("SearchResultList") ?? throw new InvalidOperationException("没有结果列表"),
            window.FindControl<Border>("SearchResultPanel") ?? throw new InvalidOperationException("没有结果面板"));
    }

    private static void SetKeyword(TextBox searchBox, string keyword)
    {
        searchBox.Text = keyword;
        Dispatcher.UIThread.RunJobs();
    }

    private static List<SettingsSearchHit> SearchHits(ListBox resultList) =>
        resultList.ItemsSource?.Cast<SettingsSearchHit>().ToList() ?? [];

    // 与 SettingsWindowScreenshotTests 同一套最小组装：真页数据 + 取页委托，不碰 App.ViewModel
    private static SettingsWindow BuildWindow()
    {
        RecordingMessageService messages = new();
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IMessageService>(messages)
            .AddSingleton(new ApplicationUpdateService { HasChecked = true })
            .BuildServiceProvider();

        return new SettingsWindow(
            ActivatorUtilities.CreateInstance<GeneralSettingViewModel>(services),
            new RuntimeEngineSettingData(messages),
            new ShortcutSettingViewModel(),
            _ => new HelpPageData());
    }
}
