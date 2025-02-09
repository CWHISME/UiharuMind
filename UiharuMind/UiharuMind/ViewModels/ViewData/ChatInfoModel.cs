using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.Views;
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

    public ChatInfoModel()
    {
        var viewModel = App.ViewModel.GetViewModel<ChatViewModel>();
        OnChatSessionChanged(viewModel.ChatSession);
        viewModel.OnEventChatSessionChanged += OnChatSessionChanged;
    }

    private void OnChatSessionChanged(ChatSessionViewData? chatSessionViewData)
    {
        // HasUserCard = !chatSessionViewData.ChatSession.CharacterData.IsTool;
        ChatPluginList.Clear();

        if (chatSessionViewData != null)
        {
            //用户角色
            if (!chatSessionViewData.ChatSession.CharacterData.IsTool)
            {
                var plugin = GetPlugin<ChatPlugin_UserCharacterCardData>(chatSessionViewData);
                ChatPluginList.Add(plugin);
            }

            //角色
            ChatPluginList.Add(GetPlugin<ChatPlugin_ChatCharacterInfoData>(chatSessionViewData));

            ChatPluginList.Add(GetPlugin<ChatPlugin_TranslationData>(chatSessionViewData));

            //本地模型设置
            ChatPluginList.Add(GetPlugin<ChatPlugin_LocalModelParamsData>(chatSessionViewData));

            //对话参数
            ChatPluginList.Add(GetPlugin<ChatPlugin_ChatParamsData>(chatSessionViewData));
            // ChatPluginList.Add(GetPlugin<ChatPlugin_CharacterFuncBtnData>(chatSessionViewData));
        }

        OnEventChatSessionChanged?.Invoke();
    }

    private ChatPluginBase GetPlugin<T>(ChatSessionViewData chatSessionViewData) where T : ChatPluginBase, new()
    {
        if (ChatPluginsCacheDict.TryGetValue(typeof(T), out var chatPlugin))
        {
            chatPlugin.SetSessonData(chatSessionViewData);
            return chatPlugin;
        }

        var plugin = new T();
        ChatPluginsCacheDict[typeof(T)] = plugin;
        plugin.SetSessonData(chatSessionViewData);
        return plugin;
    }
}