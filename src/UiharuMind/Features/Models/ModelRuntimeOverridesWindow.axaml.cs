using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI.Core;

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
        ModelRuntimeOverridesWindow window = new() { DataContext = ModelRuntimeOverridesView.CreateData(model) };
        return window.ShowDialog(owner);
    }

    public ModelRuntimeOverridesWindow()
    {
        InitializeComponent();
    }

    private void DoneButton_Click(object? sender, RoutedEventArgs e) => Close();
}
