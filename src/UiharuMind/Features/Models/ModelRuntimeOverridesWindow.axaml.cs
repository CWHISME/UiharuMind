using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;

namespace UiharuMind.Features.Models;

public partial class ModelRuntimeOverridesWindow : Window
{
    /// <summary>
    /// 打开某个本地模型的运行参数
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="model">本地模型</param>
    public static Task ShowWindow(Window owner, ModelRunningData model)
    {
        ModelRuntimeSettingConfig config = ModelRuntimeSettingConfig.Current;
        string name = model.ModelName;
        ModelRuntimeOverridesWindow window = new()
        {
            DataContext = new ModelRuntimeOverridesViewData(name, config, config.Save,
                () => LlmManager.Instance.AnalyzeLoadRisk(name), model.IsRunning,
                (model.ModelInfo as GGufModelInfo)?.LayerCount ?? 0)
        };
        return window.ShowDialog(owner);
    }

    public ModelRuntimeOverridesWindow()
    {
        InitializeComponent();
    }

    private void DoneButton_Click(object? sender, RoutedEventArgs e) => Close();
}
