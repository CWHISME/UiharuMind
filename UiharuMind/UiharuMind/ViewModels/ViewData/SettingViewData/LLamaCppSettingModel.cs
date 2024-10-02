using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

namespace UiharuMind.ViewModels.SettingViewData;

public class LLamaCppSettingModel : ObservableObject
{
    public LLamaCppSettingConfig LLamaCppSettingConfig => LlmManager.Instance.LLamaCppServer.Config;
}