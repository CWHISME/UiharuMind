using UiharuMind.Features.Models;
using UiharuMind.Features.Settings.Engines;
using UiharuMind.Generated;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Features.Settings.Audio;

/// <summary>
/// 本地模型（语音）页：骨架见 <see cref="LocalEngineSettingView"/>，这里只给文案与运行参数
/// </summary>
public sealed class AudioEngineSettingView : LocalEngineSettingView
{
    private static readonly LocalEngineSettingTexts Texts = new(LangKey.AudioEngineTitle, LangKey.AudioEngineDesc,
        LangKey.AudioModelsTitle, LangKey.AudioModelsDesc, LangKey.AudioManageModels, LangKey.AudioManageModelsDesc,
        LangKey.AudioManageModelsBtn, LangKey.AudioRuntimeTitle, LangKey.AudioRuntimeDesc);

    public AudioEngineSettingView() : this(App.ViewModel.GetViewModel<SettingViewModel>().AudioEngineSettingData)
    {
    }

    /// <param name="viewModel">页数据（无头测试自备）</param>
    public AudioEngineSettingView(AudioEngineSettingData viewModel) : base(Texts, "AudioEngineSetting", viewModel.Engine,
        new AudioCppRuntimeSettingsView { DataContext = viewModel.Runtime },
        () => App.ShowMainPage<ModelPageData>(MenuPages.MenuModelKey)?.ShowAudioModels())
    {
    }
}
