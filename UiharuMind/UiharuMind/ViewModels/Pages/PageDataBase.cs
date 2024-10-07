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
using UiharuMind.Core;
using UiharuMind.Core.AI;
using UiharuMind.Core.LLamaCpp;
using UiharuMind.Core.LLamaCpp.Data;
using UiharuMind.Services;
using UiharuMind.ViewModels.Interfaces;

namespace UiharuMind.ViewModels.Pages;

public abstract partial class PageDataBase : ViewModelBase, IViewControl
{
    private Control? _view;

    public Control View
    {
        get { return _view ??= CreateView; }
    }
    
    // public LLamaCppSettingConfig LLamaConfig => LlmManager.Instance.LLamaCppServer.Config;
    
    // public void ShowNotification(string message)
    // {
    //     ShowNotification("Information", message, NotificationType.Information);
    // }

    // public void ShowNotification(string title, string message, NotificationType type)
    // {
    //     App.MessageService.Show(new Notification(title, message, type));
    // }

    protected abstract Control CreateView { get; }
}