using System.Collections.ObjectModel;

namespace UiharuMind.ViewModels;

public class MenuViewData
{
    public ObservableCollection<MenuItemViewData> MenuItems { get; set; } = new()
    {
        new() { MenuHeader = "主页", MenuIconName = "Home", Key = MenuKeys.MenuMainKey },
        new() { MenuHeader = "Separator", IsSeparator = true },
        new() { MenuHeader = "聊天", MenuIconName = "Chat", Key = MenuKeys.MenuChatKey },
        // new() { MenuHeader = "翻译", MenuIconName = "Translate", Key = MenuKeys.MenuTranslateKey },
        // new() { MenuHeader = "语音", MenuIconName = "Voice", Key = MenuKeys.MenuKeyAudio, Status = "Goods" },
        new() { MenuHeader = "模型", MenuIconName = "Folder", Key = MenuKeys.MenuModelKey },
        // new() { MenuHeader = "绘图", MenuIconName = "Image", Key = MenuKeys.MenuKeyDraw },
        new() { MenuHeader = "设置", MenuIconName = "Setting", Key = MenuKeys.MenuSettingKey },
    };
}

public static class MenuKeys
{
    public const string MenuMainKey = "HomeKey";
    public const string MenuModelKey = "ModelKey";
    public const string MenuChatKey = "ChatKey";
    public const string MenuTranslateKey = "TranslateKey";
    public const string MenuVoiceKey = "VoiceKey";
    public const string MenuPaintKey = "PaintKey";
    public const string MenuSettingKey = "SettingKey";
}