using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core;
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


    public FilesService FilesService => App.FilesService;
    public LLamaCppServerKernal LlamaService => UiharuCoreManager.Instance.LLamaCppServer;

    public LLamaCppSettingConfig LLamaConfig => UiharuCoreManager.Instance.LLamaCppServer.Config;


    public void ShowNotification(string message)
    {
        ShowNotification("Information", message, NotificationType.Information);
    }

    public void ShowNotification(string title, string message, NotificationType type)
    {
        App.NotificationManager.Show(new Notification(title, message, type));
    }

    protected abstract Control CreateView { get; }
}