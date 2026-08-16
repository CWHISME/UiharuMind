using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Settings;

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
    private readonly SettingsWriteBack _writeBack = new(() => ConfigManager.Instance.DebugSetting.Save()); //写回闸门

    // ConfigManager.Instance.DebugSetting.LogTypeInfo
    [ObservableProperty] private string[] _logLevelList; //= new ObservableCollection<string>();
    [ObservableProperty] private int _logSelectedTypeIndex;

    public DebugSettingViewModel()
    {
        int max = (int)ELogType.Error + 1;
        LogLevelList = new string[max];
        for (int i = 0; i < max; i++)
        {
            LogLevelList[i] = ((ELogType)i).ToString();
        }

        //回填不该算用户改动:此前这里一进设置页就把 DebugSetting 原样重写一遍落盘
        using (_writeBack.BeginLoad())
        {
            LogSelectedTypeIndex = (int)ConfigManager.Instance.DebugSetting.LogTypeInfo;
        }
    }

    partial void OnLogSelectedTypeIndexChanged(int value)
    {
        ConfigManager.Instance.DebugSetting.LogTypeInfo =
            (ELogType)value; //(ELogType)Enum.Parse(typeof(ELogType), value);
        _writeBack.Save();
    }
}