using System.Collections.ObjectModel;

namespace UiharuMind.ViewModels;

public class MenuViewData : ViewModelBase
{
    public ObservableCollection<MenuItemViewData> MenuItems { get; set; } = new()
    {
        new() { MenuHeader = "主页", Key = MenuKeys.MenuMainPage, IsSeparator = false },
        new() { MenuHeader = "聊天", Key = MenuKeys.MenuKeyCaht, IsSeparator = true },
        new() { MenuHeader = "语音", Key = MenuKeys.MenuKeyAudio, Status = "Goods" },
        new() { MenuHeader = "绘图", Key = MenuKeys.MenuKeyDraw },
        new() { MenuHeader = "工具", Key = MenuKeys.MenuKeyTools },
        new() { MenuHeader = "设置", Key = MenuKeys.MenuKeySetting },
    };
}

public static class MenuKeys
{
    public const string MenuMainPage = "主页 Key";
    public const string MenuKeyCaht = "聊天 Key";
    public const string MenuKeyAudio = "语音 Key";
    public const string MenuKeyDraw = "绘图 Key";
    public const string MenuKeyTools = "工具 Key";
    public const string MenuKeySetting = "设置 Key";
}