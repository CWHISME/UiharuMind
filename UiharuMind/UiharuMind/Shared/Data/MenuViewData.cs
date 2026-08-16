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
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
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
            new() { MenuHeaderResourceKey = nameof(Lang.MenuAgentKey), MenuIconName = "house", Key = MenuPages.MenuAgentKey },
            new() { MenuHeaderResourceKey = nameof(Lang.MenuChatKey), MenuIconName = "message-circle-more", Key = MenuPages.MenuChatKey },
            new() { MenuHeaderResourceKey = nameof(Lang.MenuCharacterKey), MenuIconName = "users-round", Key = MenuPages.MenuCharacterKey },
        // new() { MenuHeader = Lang.MenuTranslateKey, MenuIconName ="Translate", Key = MenuKeys.MenuTranslateKey },
        // new() { MenuHeader = "语音", MenuIconName = "Voice", Key = MenuKeys.MenuKeyAudio, Status = "Goods" },
            new() { MenuHeaderResourceKey = nameof(Lang.MenuModelKey), MenuIconName = "folder-cog", Key = MenuPages.MenuModelKey },
            new() { MenuHeaderResourceKey = "MenuServicesKey", MenuIconName = "cog", Key = MenuPages.MenuServicesKey },
            new() { MenuHeaderResourceKey = nameof(Lang.MenuLogKey), MenuIconName = "book-search", Key = MenuPages.MenuLogKey },
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
    /// 把「哪一页有会话在忙」聚合到对应的菜单项上：会话档位决定它归智能体页还是对话页
    /// （<see cref="SessionManager.KindOf"/>），与两页各自的列表口径同一份判据。
    /// </summary>
    private void RefreshRunState()
    {
        bool agentBusy = false;
        bool agentApproval = false;
        bool chatBusy = false;
        bool chatApproval = false;

        foreach ((string sessionId, ESessionRunState state) in SessionManager.Instance.Running.ActiveSessions())
        {
            //还没进索引的会话(首轮发送前的临时态)没有档位可判,跳过
            if (SessionManager.Instance.GetMeta(sessionId) is not { } meta) continue;
            ECharacterKind kind = SessionManager.KindOf(meta);
            bool awaiting = state == ESessionRunState.AwaitingApproval;
            if (kind.IsAgent())
            {
                agentBusy = true;
                agentApproval |= awaiting;
            }
            else if (kind.IsChat())
            {
                chatBusy = true;
                chatApproval |= awaiting;
            }
        }

        Apply(MenuPages.MenuAgentKey, agentBusy, agentApproval);
        Apply(MenuPages.MenuChatKey, chatBusy, chatApproval);
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
