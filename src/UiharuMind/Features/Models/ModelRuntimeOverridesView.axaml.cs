using Avalonia.Controls;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;

namespace UiharuMind.Features.Models;

/// <summary>
/// 某个本地模型自己的运行参数各行（每行可跟随全局或单独设）与估算；运行参数窗口与对话页的快捷弹窗共用
/// </summary>
public partial class ModelRuntimeOverridesView : UserControl
{
    public ModelRuntimeOverridesView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 给某个本地模型建一份参数数据（改动写进全局运行设置的按模型覆写并落盘）
    /// </summary>
    /// <param name="model">本地模型</param>
    /// <returns>参数数据</returns>
    public static ModelRuntimeOverridesViewData CreateData(ModelRunningData model)
    {
        ModelRuntimeSettingConfig config = ModelRuntimeSettingConfig.Current;
        string name = model.ModelName;
        GGufModelInfo? gguf = model.ModelInfo as GGufModelInfo;
        return new ModelRuntimeOverridesViewData(name, config, config.Save,
            () => LlmManager.Instance.AnalyzeLoadRisk(name), model.IsRunning,
            gguf?.LayerCount ?? 0, trainedContextLength: gguf?.ContextLength ?? 0);
    }
}
