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
using UiharuMind.Core.Core.Utils;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public partial class HelpPageData : PageDataBase
{
    public string HelpText { get; set; }

    public HelpPageData()
    {
        HelpText = EmbeddedResourcesUtils.Read("Help.md");
    }

    protected override Control CreateView => new HelpPage();
}