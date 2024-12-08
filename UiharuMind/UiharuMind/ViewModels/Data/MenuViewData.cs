/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Collections.ObjectModel;
using UiharuMind.Resources.Lang;

namespace UiharuMind.ViewModels;

public class MenuViewData
{
    public ObservableCollection<MenuItemViewData> MenuItems { get; set; } = new()
    {
        new() { MenuHeader = Lang.MenuMainKey, MenuIconName = "Home", Key = MenuPages.MenuMainKey },
        new() { MenuHeader = "Separator", IsSeparator = true },
        new() { MenuHeader = Lang.MenuChatKey, MenuIconName = "Chat", Key = MenuPages.MenuChatKey },
        // new() { MenuHeader = Lang.MenuTranslateKey, MenuIconName ="Translate", Key = MenuKeys.MenuTranslateKey },
        // new() { MenuHeader = "语音", MenuIconName = "Voice", Key = MenuKeys.MenuKeyAudio, Status = "Goods" },
        new() { MenuHeader = Lang.MenuModelKey, MenuIconName = "Folder", Key = MenuPages.MenuModelKey },
        new() { MenuHeader = Lang.MenuLogKey, MenuIconName = "BookInformationRegular", Key = MenuPages.MenuLogKey },
        // new() { MenuHeader = "绘图", MenuIconName = "Image", Key = MenuKeys.MenuKeyDraw },
        new() { MenuHeader = Lang.MenuSettingKey, MenuIconName = "Setting", Key = MenuPages.MenuSettingKey },
    };
}

// public static class MenuKeys
// {
//     public const string MenuMainKey = "HomeKey";
//     public const string MenuModelKey = "ModelKey";
//     public const string MenuChatKey = "ChatKey";
//     public const string MenuTranslateKey = "TranslateKey";
//     public const string MenuVoiceKey = "VoiceKey";
//     public const string MenuPaintKey = "PaintKey";
//     public const string MenuLogKey = "LogKey";
//     public const string MenuSettingKey = "SettingKey";
//     public const string MenuAboutKey = "AboutKey";
// }