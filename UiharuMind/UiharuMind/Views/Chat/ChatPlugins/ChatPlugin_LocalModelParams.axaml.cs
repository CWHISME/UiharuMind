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

public partial class ChatPlugin_LocalModelParams : UserControl
{
    public ChatPlugin_LocalModelParams()
    {
        InitializeComponent();
    }
}

public partial class ChatPlugin_LocalModelParamsData : ChatPluginDataBase<ChatPlugin_LocalModelParams>
{

    public ChatPlugin_LocalModelParamsData()
    {
    }
    
}