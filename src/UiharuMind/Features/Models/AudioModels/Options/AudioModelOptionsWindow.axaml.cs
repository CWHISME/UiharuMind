using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UiharuMind.Features.Models.AudioModels.Options;

/// <summary>
/// 本地语音模型的参数窗；逻辑在 <see cref="AudioModelOptionsViewData"/>，这里只管开关窗
/// </summary>
public partial class AudioModelOptionsWindow : Window
{
    /// <summary>构造窗口（经 <see cref="ShowWindow"/> 打开）</summary>
    public AudioModelOptionsWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as AudioModelOptionsViewData)?.Save();
    }

    /// <summary>
    /// 打开参数窗，关窗时存盘
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="viewData">参数窗的数据</param>
    /// <returns>关窗后完成</returns>
    public static Task ShowWindow(Window owner, AudioModelOptionsViewData viewData) =>
        new AudioModelOptionsWindow { DataContext = viewData }.ShowDialog(owner);

    private void Done_Click(object? sender, RoutedEventArgs e) => Close();
}
