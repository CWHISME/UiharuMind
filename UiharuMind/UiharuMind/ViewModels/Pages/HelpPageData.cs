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
using UiharuMind.Core.Core.Utils;
using UiharuMind.Services;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

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

    private static string ReadHelpDocument(string? languageCode)
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
