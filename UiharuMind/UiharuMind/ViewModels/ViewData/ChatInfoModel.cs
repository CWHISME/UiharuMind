using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.ViewModels.Chat;
using UiharuMind.Shared.Shell;
using UiharuMind.Views.Chat.ChatPlugins;
using UiharuMind.Views.Common.ChatPlugins;
using UiharuMind.Views.Windows.Characters;

namespace UiharuMind.ViewModels.ViewData;

public partial class ChatInfoModel : ViewModelBase
{
    // [ObservableProperty] private bool _hasUserCard;

    public readonly List<ChatPluginBase> ChatPluginList = new List<ChatPluginBase>();
    public readonly Dictionary<Type, ChatPluginBase> ChatPluginsCacheDict = new Dictionary<Type, ChatPluginBase>();

    public event Action? OnEventChatSessionChanged;

    /// <summary>
    /// 切换插件面板对应的会话(由聊天页面壳在会话选择变化时调用)
    /// </summary>
    /// <param name="chatSessionViewData">会话视图数据,为空清空面板</param>
    public void SetSession(ChatSessionItemViewData? chatSessionViewData)
    {
        OnChatSessionChanged(chatSessionViewData);
    }

    /// <summary>
    /// 通知一轮生成开始
    /// </summary>
    public void NotifyChatBegin()
    {
        foreach (var plugin in ChatPluginList)
        {
            plugin.OnChatBegin();
        }
    }

    /// <summary>
    /// 通知一轮生成结束
    /// </summary>
    public void NotifyChatEnd()
    {
        foreach (var plugin in ChatPluginList)
        {
            plugin.OnChatEnd();
        }
    }

    private void OnChatSessionChanged(ChatSessionItemViewData? chatSessionViewData)
    {
        ChatPluginList.Clear();

        if (chatSessionViewData != null)
        {
            //用户角色:纯提示词角色(翻译、识图等)不注入用户人格,也就不需要这块面板
            if (!chatSessionViewData.ChatSession.CharacterData.IsPurePromptCharacter)
            {
                var plugin = GetPlugin<ChatPlugin_UserCharacterCardData>(chatSessionViewData);
                ChatPluginList.Add(plugin);
            }

            //角色
            // ChatPluginList.Add(GetPlugin<ChatPlugin_ChatCharacterInfoData>(chatSessionViewData));

            ChatPluginList.Add(GetPlugin<ChatPlugin_TranslationData>(chatSessionViewData));

            //对话参数
            ChatPluginList.Add(GetPlugin<ChatPlugin_ChatParamsData>(chatSessionViewData));
            // ChatPluginList.Add(GetPlugin<ChatPlugin_CharacterFuncBtnData>(chatSessionViewData));
        }

        OnEventChatSessionChanged?.Invoke();
    }

    private ChatPluginBase GetPlugin<T>(ChatSessionItemViewData chatSessionViewData) where T : ChatPluginBase, new()
    {
        if (ChatPluginsCacheDict.TryGetValue(typeof(T), out var chatPlugin))
        {
            chatPlugin.SetSessionData(chatSessionViewData);
            return chatPlugin;
        }

        var plugin = new T();
        ChatPluginsCacheDict[typeof(T)] = plugin;
        plugin.SetSessionData(chatSessionViewData);
        return plugin;
    }
}
