using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs;
using UiharuMind.ViewModels.SettingViewData;

namespace UiharuMind.ViewModels.ViewData;

public class SettingViewModel : ViewModelBase
{
    // public LLamaCppSettingConfig LLamaCppSettingConfig => LlmManager.Instance.LLamaCppConfig;

    public QuickToolSetting QuickToolSettingConfig => ConfigManager.Instance.QuickToolSetting;

    public RuntimeEngineSettingData RuntimeEngineSettingData { get; } = new();
}