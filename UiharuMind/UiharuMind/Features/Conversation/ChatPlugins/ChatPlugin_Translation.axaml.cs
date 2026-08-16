using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Resources.Lang;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.SessionList;

namespace UiharuMind.Features.Conversation.ChatPlugins;

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
    [ObservableProperty] private string _selectedLanguage = Lang.AutoDetect;

    public ChatPlugin_TranslationData()
    {
        var cultures = CultureInfo.GetCultures(CultureTypes.NeutralCultures);
        _languages.Add(Lang.AutoDetect);
        foreach (var culture in cultures)
        {
            if (string.IsNullOrEmpty(culture.Name)) continue;
            _languages.Add(culture.DisplayName);
        }
    }

    protected override void OnChatSessionChanged(SessionListItem chatSessionViewData)
    {
        base.OnChatSessionChanged(chatSessionViewData);
        string? lastLanguage = null;
        if (ChatSessionCurrentViewData.Session.CustomParams.TryGetValue(CharacterData.ParamsNameLanguage,
                out object? last))
        {
            lastLanguage = last?.ToString();
        }

        if (lastLanguage == null)
        {
            SelectedLanguage = Lang.AutoDetect;
            return;
        }

        if (Languages.Contains(lastLanguage))
            SelectedLanguage = lastLanguage;
        else SelectedLanguage = Languages.Count > 0 ? Languages[0] : "";
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (value == Lang.AutoDetect)
        {
            ChatSessionCurrentViewData.Session.CustomParams.Remove(CharacterData.ParamsNameLanguage);
            return;
        }

        ChatSessionCurrentViewData.Session.CustomParams[CharacterData.ParamsNameLanguage] = value;
    }
}
