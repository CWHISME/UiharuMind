using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core;
using UiharuMind.Core.Configs;

namespace UiharuMind.ViewModels.SettingViewData;

public class QuickToolSettingViewModel : ObservableObject
{
    public QuickToolSetting SettingConfig => UiharuCoreManager.Instance.QuickToolSetting;
}