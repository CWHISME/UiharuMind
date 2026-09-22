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
using Avalonia.Threading;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Data;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Shared.Data;

public class MenuViewData
{
    public ObservableCollection<MenuItemViewData> MenuItems { get; set; }

    public MenuViewData()
    {
        MenuItems = new ObservableCollection<MenuItemViewData>
        {
            // 对话页已合并：普通对话与智能体在同一页，左栏内切换类型
            new() { MenuHeaderResourceKey = nameof(LangKey.MenuConversationKey), MenuIconName = "message-circle-more", Key = MenuPages.MenuConversationKey },
            new() { MenuHeaderResourceKey = nameof(LangKey.MenuCharacterKey), MenuIconName = "users-round", Key = MenuPages.MenuCharacterKey },
        // new() { MenuHeader = Loc.Text(LangKey.MenuTranslateKey), MenuIconName ="Translate", Key = MenuKeys.MenuTranslateKey },
        // new() { MenuHeader = "语音", MenuIconName = "Voice", Key = MenuKeys.MenuKeyAudio, Status = "Goods" },
            new() { MenuHeaderResourceKey = nameof(LangKey.MenuModelKey), MenuIconName = "folder-open", Key = MenuPages.MenuModelKey },
            // new() { MenuHeaderResourceKey = nameof(LangKey.MenuServicesKey), MenuIconName = "cog", Key = MenuPages.MenuServicesKey },
            new() { MenuHeaderResourceKey = nameof(LangKey.MenuLogKey), MenuIconName = "book-search", Key = MenuPages.MenuLogKey },
        // new() { MenuHeader = "绘图", MenuIconName = "Image", Key = MenuKeys.MenuKeyDraw },
        };

        RefreshLanguage();
        LocalizationManager.Instance.LanguageChanged += RefreshLanguage;

        RefreshRunState();
        // 运行态变化可能来自后台线程(定时任务的无头执行),菜单项是界面绑定的
        SessionManager.Instance.Running.StateChanged += _ =>
            Dispatcher.UIThread.Post(RefreshRunState);
    }

    /// <summary>
    /// 把「有会话在忙 / 有待审批」聚合到对话页菜单项上。合并后不再区分普通对话与智能体——
    /// 角标只说「有东西在跑」，进去了由页内会话列表与右栏面板说清是哪一类
    /// </summary>
    private void RefreshRunState()
    {
        bool busy = false;
        bool awaitingApproval = false;

        foreach ((string sessionId, ESessionRunState state) in SessionManager.Instance.Running.ActiveSessions())
        {
            //还没进索引的会话(首轮发送前的临时态)没有档位可判,跳过
            if (SessionManager.Instance.GetMeta(sessionId) is null) continue;
            bool awaiting = state == ESessionRunState.AwaitingApproval;
            busy = true;
            if (awaiting) awaitingApproval = true;
        }

        Apply(MenuPages.MenuConversationKey, busy, awaitingApproval);
    }

    private void Apply(MenuPages page, bool isBusy, bool isAwaitingApproval)
    {
        foreach (MenuItemViewData item in MenuItems)
        {
            if (item.Key != page) continue;
            item.IsBusy = isBusy;
            item.IsAwaitingApproval = isAwaitingApproval;
        }
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
