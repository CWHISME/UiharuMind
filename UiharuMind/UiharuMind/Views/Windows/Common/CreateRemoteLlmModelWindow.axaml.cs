using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Interfaces;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core.Extensions;
using UiharuMind.Core.RemoteOpenAI;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.ViewModels;

namespace UiharuMind.Views.Windows.Common;

public partial class CreateRemoteLlmModelWindow : Window
{
    private readonly IMessageService _messageService;
    public static async Task<RemoteModelInfo?> ShowWindow(Window owner, RemoteModelInfo? remoteModelInfo = null)
    {
        var window = new CreateRemoteLlmModelWindow
        {
            DataContext = new CreateRemoteLlmModelWindowViewModel(remoteModelInfo)
        };
        return await window.ShowDialog<RemoteModelInfo>(owner);
    }

    public CreateRemoteLlmModelWindow()
    {
        _messageService = App.Services.GetRequiredService<IMessageService>();
        InitializeComponent();
        SizeToContent = SizeToContent.Height;
    }

    private async void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        var cfg = (DataContext as CreateRemoteLlmModelWindowViewModel)?.RemoteModelInfo;
        if (cfg == null) return;
        if (string.IsNullOrEmpty(cfg.ModelName))
        {
            await _messageService.ShowInfoAsync(Lang.NotInputModelNameTips);
            return;
        }

        // if (LlmManager.Instance.CacheModelDictionary.ContainsKey(cfg.ModelName))
        // {
        //     await _messageService.ShowInfoAsync(Lang.RepeatModelNameTips);
        //     return;
        // }

        if (string.IsNullOrEmpty(cfg.ApiKey))
        {
            await _messageService.ShowInfoAsync(Lang.NotInputModelApiKey);
            return;
        }

        Close(cfg);
    }
}

public partial class CreateRemoteLlmModelWindowViewModel : ObservableObject
{
    
    public ModelConfig CustomeConfig { get; } = new ModelConfig { Name = Lang.CustomConfig, Type = typeof(RemoteModelConfig) };
    public ObservableCollection<ModelConfig> ModelConfigTypes { get; set; } = new ObservableCollection<ModelConfig>();

    [ObservableProperty] private bool _hasModelConfig;
    [ObservableProperty] private RemoteModelInfo? _remoteModelInfo;

    public CreateRemoteLlmModelWindowViewModel(RemoteModelInfo? remoteModelInfo = null)
    {
        _remoteModelInfo = remoteModelInfo;
        if (remoteModelInfo == null)
        {
            var types = typeof(LlmManager).Assembly.GetTypesOfInterface(nameof(IRemoteModelConfig));
            // var modelConfigs = new List<ModelConfig>();
            foreach (var type in types)
            {
                ModelConfigTypes.Add(new ModelConfig { Name = type.GetDescription(), Type = type });
            }

            //ModelConfigTypes.Add(new ModelConfig { Name = Lang.CustomConfig, Type = typeof(RemoteModelConfig) });
            // ModelConfigTypes = modelConfigs;
        }
        // _remoteModelInfo ??= new RemoteModelInfo();
    }

    [RelayCommand]
    public void CreateRemoteModel(ModelConfig modelConfig)
    {
        if (Activator.CreateInstance(modelConfig.Type) is not BaseRemoteModelConfig config)
        {
            return;
        }

        RemoteModelInfo = new RemoteModelInfo()
        {
            Config = config
        };
    }

    public struct ModelConfig
    {
        public string Name { get; set; }
        public Type Type { get; set; }
    }
}
