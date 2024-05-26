using System.Collections.ObjectModel;

namespace UiharuMind.ViewModels;

public class MenuViewData
{
    public ObservableCollection<MenuItemViewData> MenuItems { get; set; } = new()
    {
        new() { MenuHeader = "主页", MenuIconName = "Home", Key = MenuKeys.MenuMainPage },
        new() { MenuHeader = "Separator", IsSeparator = true },
        new() { MenuHeader = "聊天", MenuIconName = "Chat", Key = MenuKeys.MenuKeyChat },
        new() { MenuHeader = "翻译", MenuIconName = "Translate", Key = MenuKeys.MenuKeyChat },
        new() { MenuHeader = "语音", MenuIconName = "Voice", Key = MenuKeys.MenuKeyAudio, Status = "Goods" },
        // new() { MenuHeader = "绘图", MenuIconName = "Image", Key = MenuKeys.MenuKeyDraw },
        new() { MenuHeader = "模型", MenuIconName = "Folder", Key = MenuKeys.MenuKeySetting },
    };
}

public static class MenuKeys
{
    public const string MenuMainPage = "主页 Key";
    public const string MenuKeyChat = "聊天 Key";
    public const string MenuKeyAudio = "语音 Key";
    public const string MenuKeyDraw = "绘图 Key";
    public const string MenuKeySetting = "设置 Key";
}