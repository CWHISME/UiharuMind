/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using Avalonia.Controls;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public class LogPageData : PageDataBase
{
    protected override Control CreateView => new LogPage();
}