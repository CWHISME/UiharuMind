using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Features.AutoClick;
using UiharuMind.Features.Clipboard;
using UiharuMind.Features.Conversation.SidePanels;
using UiharuMind.Features.LogViewer;
using UiharuMind.Features.QuickTools;
using UiharuMind.Features.Settings;
using UiharuMind.Shared.Shell;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// GetViewModel / GetPage 的显式工厂收口（AOT 前置，清单 #4）：
/// 工厂字典必须覆盖全部使用点（新增忘登记时测试红），缓存语义不变（同 key 同实例）。
/// 各 ViewModel 的构造行为依赖 App 运行环境（AppPaths / App.Clipboard / App.Services 等全局静态），
/// 不在单测里物化，由 UI 冒烟兜底；StartupPageTests 是 GetPage 的现成回归。
/// </summary>
public class MainViewModelFactoryTests
{
    [Fact]
    public void ViewModelFactories_CoverAllNineConsumers()
    {
        Type[] expected = [typeof(QuickAutoClickViewModel), typeof(ChatInfoModel), typeof(ClipboardHistoryViewModel),
            typeof(LogViewModel), typeof(SearchViewModel), typeof(RadialMenuModel),
            typeof(SettingViewModel), typeof(GeneralSettingViewModel), typeof(ShortcutSettingViewModel)];

        Assert.Equal(expected.OrderBy(t => t.Name), MainViewModel.ViewModelFactories.Keys.OrderBy(t => t.Name));
    }

    [Fact]
    public void PageFactories_CoverAllSevenMenuKeys()
    {
        MenuPages[] expected = [MenuPages.MenuConversationKey, MenuPages.MenuCharacterKey, MenuPages.MenuTranslateKey,
            MenuPages.MenuModelKey, MenuPages.MenuServicesKey, MenuPages.MenuLogKey, MenuPages.MenuHelpKey];

        Assert.Equal(expected, MainViewModel.PageFactories.Keys);
    }

    [Fact]
    public void GetViewModel_SameType_ReturnsSameInstance()
    {
        var vm = new MainViewModel(new ServiceCollection().BuildServiceProvider());

        // ShortcutSettingViewModel 是纯属性无依赖的 ViewModel，测试环境可安全物化；
        // 其余类型构造依赖 App 全局静态（容器/路径/剪贴板），由 UI 冒烟兜底
        Assert.Same(vm.GetViewModel<ShortcutSettingViewModel>(), vm.GetViewModel<ShortcutSettingViewModel>());
    }

    [Fact]
    public void GetViewModel_UnregisteredType_Throws()
    {
        var vm = new MainViewModel(new ServiceCollection().BuildServiceProvider());

        Assert.Throws<KeyNotFoundException>(() => vm.GetViewModel<UnregisteredViewModel>());
    }

    private sealed class UnregisteredViewModel : ViewModelBase
    {
    }
}
