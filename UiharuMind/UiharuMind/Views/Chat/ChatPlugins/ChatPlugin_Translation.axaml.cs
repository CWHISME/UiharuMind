using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.Utils;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common.ChatPlugins;
using UiharuMind.Views.Windows.Characters;

namespace UiharuMind.Views.Chat.ChatPlugins;

public partial class ChatPlugin_Translation : UserControl
{
    public ChatPlugin_Translation()
    {
        InitializeComponent();
    }
}

public partial class ChatPlugin_TranslationData : ChatPluginDataBase<ChatPlugin_Translation>
{
    [ObservableProperty] private List<string> _languages = new List<string>();

    public ChatPlugin_TranslationData()
    {
        var cultures = CultureInfo.GetCultures(CultureTypes.NeutralCultures);
        foreach (var culture in cultures)
        {
            _languages.Add(culture.DisplayName);
        }
    }
}