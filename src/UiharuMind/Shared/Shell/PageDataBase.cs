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
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Interfaces;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core;

namespace UiharuMind.Shared.Shell;

public abstract partial class PageDataBase : ViewModelBase, IViewControl
{
    private Control? _view;

    public Control View
    {
        get { return _view ??= CreateView; }
    }
    
    // public void ShowNotification(string message)
    // {
    // }

    protected abstract Control CreateView { get; }
}
