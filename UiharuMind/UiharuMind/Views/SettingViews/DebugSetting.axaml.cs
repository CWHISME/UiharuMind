using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.ViewModels;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.SettingViews;

public partial class DebugSetting : UserControl
{
    public DebugSetting()
    {
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<DebugSettingViewModel>();
    }
}

public partial class DebugSettingViewModel : ViewModelBase
{
    // ConfigManager.Instance.DebugSetting.LogTypeInfo
    [ObservableProperty] private string[] _logLevelList; //= new ObservableCollection<string>();
    [ObservableProperty] private int _logSelecetedTypeIndex;

    public DebugSettingViewModel()
    {
        int max = (int)ELogType.Error + 1;
        LogLevelList = new string[max];
        for (int i = 0; i < max; i++)
        {
            LogLevelList[i] = ((ELogType)i).ToString();
        }

        LogSelecetedTypeIndex = (int)ConfigManager.Instance.DebugSetting.LogTypeInfo;
    }

    partial void OnLogSelecetedTypeIndexChanged(int value)
    {
        ConfigManager.Instance.DebugSetting.LogTypeInfo =
            (ELogType)value; //(ELogType)Enum.Parse(typeof(ELogType), value);
        ConfigManager.Instance.DebugSetting.Save();
    }
}