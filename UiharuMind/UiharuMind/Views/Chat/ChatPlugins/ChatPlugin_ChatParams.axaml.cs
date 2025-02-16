using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Character;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common.ChatPlugins;

namespace UiharuMind.Views.Chat.ChatPlugins;

public partial class ChatPlugin_ChatParams : UserControl
{
    public ChatPlugin_ChatParams()
    {
        InitializeComponent();
    }
}

public partial class ChatPlugin_ChatParamsData : ChatPluginDataBase<ChatPlugin_ChatParams>
{
    [ObservableProperty] private CharacterData _character;
    // [ObservableProperty] private bool _isToolCharacter;

    protected override void OnChatSessionChanged(ChatSessionViewData chatSessionViewData)
    {
        Character = chatSessionViewData.ChatSession.CharacterData;
    }

    public override void OnChatBegin()
    {
        base.OnChatBegin();
        if (!Character.Config.ExecutionSettings.IsDirty) return;
        Character.Config.ExecutionSettings.IsDirty = false;
        Character.Save();
    }
}