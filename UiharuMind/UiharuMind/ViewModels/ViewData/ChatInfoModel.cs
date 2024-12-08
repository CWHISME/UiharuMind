using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.Views;
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
        App.ViewModel.GetViewModel<ChatViewModel>().OnEventChatSessionChanged += OnChatSessionChanged;
    }

    private void OnChatSessionChanged(ChatSessionViewData chatSessionViewData)
    {
        // HasUserCard = !chatSessionViewData.ChatSession.CharacterData.IsTool;
        ChatPluginList.Clear();
        //角色卡
        if (!chatSessionViewData.ChatSession.CharacterData.IsTool)
        {
            ChatPluginList.Add(GetPlugin<ChatPlugin_UserCharacterCardData>());
        }

        OnEventChatSessionChanged?.Invoke();
    }

    private ChatPluginBase GetPlugin<T>() where T : ChatPluginBase, new()
    {
        if (ChatPluginsCacheDict.TryGetValue(typeof(T), out var chatPlugin))
        {
            return chatPlugin;
        }

        var plugin = new T();
        ChatPluginsCacheDict[typeof(T)] = plugin;
        return plugin;
    }
}