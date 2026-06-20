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
using UiharuMind.Services;
using UiharuMind.Resources.Lang;

namespace UiharuMind.ViewModels;

public class MenuViewData
{
    public ObservableCollection<MenuItemViewData> MenuItems { get; set; }

    public MenuViewData()
    {
        MenuItems = new ObservableCollection<MenuItemViewData>
        {
            new() { MenuHeaderResourceKey = nameof(Lang.MenuMainKey), MenuIconName = "Home", Key = MenuPages.MenuMainKey },
            new() { MenuHeaderResourceKey = nameof(Lang.MenuChatKey), MenuIconName = "Chat", Key = MenuPages.MenuChatKey },
        // new() { MenuHeader = Lang.MenuTranslateKey, MenuIconName ="Translate", Key = MenuKeys.MenuTranslateKey },
        // new() { MenuHeader = "语音", MenuIconName = "Voice", Key = MenuKeys.MenuKeyAudio, Status = "Goods" },
            new() { MenuHeaderResourceKey = nameof(Lang.MenuModelKey), MenuIconName = "Folder", Key = MenuPages.MenuModelKey },
            new() { MenuHeaderResourceKey = nameof(Lang.MenuLogKey), MenuIconName = "BookInformationRegular", Key = MenuPages.MenuLogKey },
        // new() { MenuHeader = "绘图", MenuIconName = "Image", Key = MenuKeys.MenuKeyDraw },
        };

        RefreshLanguage();
        LocalizationManager.Instance.LanguageChanged += RefreshLanguage;
    }

    private void RefreshLanguage()
    {
        foreach (var menuItem in MenuItems)
        {
            if (menuItem.MenuHeaderResourceKey == null) continue;
            menuItem.MenuHeader = LocalizationManager.Instance.GetString(menuItem.MenuHeaderResourceKey);
        }
    }
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
