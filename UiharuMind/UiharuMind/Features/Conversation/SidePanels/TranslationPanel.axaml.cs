using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Resources.Lang;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Conversation.SessionList;

namespace UiharuMind.Features.Conversation.SidePanels;

public partial class TranslationPanel : UserControl
{
    public TranslationPanel()
    {
        InitializeComponent();
    }
}

/// <summary>会话详情栏的翻译语言选择。选中语言存在会话的 CustomParams 上</summary>
public partial class TranslationViewData : ObservableObject
{
    [ObservableProperty] private List<string> _languages = new List<string>();
    [ObservableProperty] private string _selectedLanguage = Lang.AutoDetect;

    private SessionListItem? _session;

    public TranslationViewData()
    {
        var cultures = CultureInfo.GetCultures(CultureTypes.NeutralCultures);
        _languages.Add(Lang.AutoDetect);
        foreach (var culture in cultures)
        {
            if (string.IsNullOrEmpty(culture.Name)) continue;
            _languages.Add(culture.DisplayName);
        }
    }

    /// <summary>切到某会话：回填它上次选的语言</summary>
    /// <param name="session">会话列表条目</param>
    public void SetSession(SessionListItem session)
    {
        _session = session;

        string? lastLanguage = null;
        if (session.Session.CustomParams.TryGetValue(CharacterData.ParamsNameLanguage, out object? last))
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
        if (_session == null) return; //回填期之外不会走到这里,防的是没会话时被绑定触发

        if (value == Lang.AutoDetect)
        {
            _session.Session.CustomParams.Remove(CharacterData.ParamsNameLanguage);
            return;
        }

        _session.Session.CustomParams[CharacterData.ParamsNameLanguage] = value;
    }
}
