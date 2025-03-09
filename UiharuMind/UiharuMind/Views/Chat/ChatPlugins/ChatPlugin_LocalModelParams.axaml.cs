using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common.ChatPlugins;

namespace UiharuMind.Views.Chat.ChatPlugins;

public partial class ChatPlugin_LocalModelParams : UserControl
{
    public ChatPlugin_LocalModelParams()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as ChatPlugin_LocalModelParamsData)?.TriggerReloading();
    }
}

public partial class ChatPlugin_LocalModelParamsData : ChatPluginDataBase<ChatPlugin_LocalModelParams>
{
    [ObservableProperty] private int _gpuLayers;
    [ObservableProperty] private bool _flashAttn;
    [ObservableProperty] private int _ctxSize;

    public ChatPlugin_LocalModelParamsData()
    {
    }

    public void TriggerReloading()
    {
        GpuLayers = LlmManager.Instance.LLamaCppServer.Config.GeneralConfig.GpuLayers;
        FlashAttn = LlmManager.Instance.LLamaCppServer.Config.GeneralConfig.FlashAttn;
        CtxSize = LlmManager.Instance.LLamaCppServer.Config.ParamsConfig.CtxSize;
    }

    protected override void OnChatSessionChanged(ChatSessionViewData chatSessionViewData)
    {
        base.OnChatSessionChanged(chatSessionViewData);
        TriggerReloading();
    }

    partial void OnGpuLayersChanged(int value)
    {
        LlmManager.Instance.LLamaCppServer.Config.GeneralConfig.GpuLayers = value;
        LlmManager.Instance.LLamaCppServer.Config.GeneralConfig.OnPropertyChanged(nameof(GpuLayers));
        LlmManager.Instance.LLamaCppServer.Config.Save();
    }

    partial void OnFlashAttnChanged(bool value)
    {
        LlmManager.Instance.LLamaCppServer.Config.GeneralConfig.FlashAttn = value;
        LlmManager.Instance.LLamaCppServer.Config.GeneralConfig.OnPropertyChanged(nameof(FlashAttn));
        LlmManager.Instance.LLamaCppServer.Config.Save();
    }

    partial void OnCtxSizeChanged(int value)
    {
        LlmManager.Instance.LLamaCppServer.Config.ParamsConfig.CtxSize = value;
        LlmManager.Instance.LLamaCppServer.Config.ParamsConfig.OnPropertyChanged(nameof(CtxSize));
        LlmManager.Instance.LLamaCppServer.Config.Save();
    }
}