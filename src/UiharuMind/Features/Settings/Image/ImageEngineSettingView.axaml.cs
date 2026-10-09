using UiharuMind.Features.Models;
using UiharuMind.Features.Settings.Engines;
using UiharuMind.Generated;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Features.Settings.Image;

/// <summary>
/// 本地模型（生图）页：骨架见 <see cref="LocalEngineSettingView"/>，这里只给文案与运行参数
/// </summary>
public sealed class ImageEngineSettingView : LocalEngineSettingView
{
    private static readonly LocalEngineSettingTexts Texts = new(LangKey.ImageEngineTitle, LangKey.ImageEngineDesc,
        LangKey.ImageModelsTitle, LangKey.ImageModelsDesc, LangKey.ImageManageModels, LangKey.ImageManageModelsDesc,
        LangKey.ImageManageModelsBtn, LangKey.ImageRuntimeTitle, LangKey.ImageRuntimeDesc);

    public ImageEngineSettingView() : this(App.ViewModel.GetViewModel<SettingViewModel>().ImageEngineSettingData)
    {
    }

    /// <param name="viewModel">页数据（无头测试自备）</param>
    public ImageEngineSettingView(ImageEngineSettingData viewModel) : base(Texts, "ImageEngineSetting", viewModel.Engine,
        new SdCppRuntimeSettingsView { DataContext = viewModel.Runtime },
        () => App.ShowMainPage<ModelPageData>(MenuPages.MenuModelKey)?.ShowImageModels())
    {
    }
}
