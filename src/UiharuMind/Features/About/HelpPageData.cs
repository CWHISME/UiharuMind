/****************************************************************************
 * Copyright (c) 2025 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2025.01.08
 ****************************************************************************/

using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Features.About;

public partial class HelpPageData : PageDataBase
{
    [ObservableProperty] private string _helpText = string.Empty;

    public HelpPageData()
    {
        LocalizationManager.Instance.LanguageChanged += RefreshHelpText;
        RefreshHelpText();
    }

    protected override Control CreateView => new HelpPage { DataContext = this };

    private void RefreshHelpText()
    {
        HelpText = ReadHelpDocument(LocalizationManager.Instance.LanguageCode);
    }

    /// <summary>
    /// 按语言读帮助文档，没有对应语言的回落中文版。内置技能 uiharu-guide 也读这一份
    /// </summary>
    /// <param name="languageCode">语言代码；为空用默认</param>
    /// <returns>帮助正文（markdown）</returns>
    internal static string ReadHelpDocument(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            try
            {
                return EmbeddedResourcesUtils.Read($"Help.{languageCode}.md");
            }
            catch (FileNotFoundException)
            {
                // Fallback below.
            }
        }

        return EmbeddedResourcesUtils.Read("Help.md");
    }
}
