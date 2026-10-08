using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI.ImageGeneration;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 本地生图模型的参数窗；表单逻辑在 <see cref="LocalImageModelEditViewData"/>
/// </summary>
public partial class LocalImageModelEditWindow : Window
{
    /// <summary>构造窗口（经 <see cref="ShowWindow"/> 打开）</summary>
    public LocalImageModelEditWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 打开参数窗
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="source">要编辑的本地模型</param>
    /// <param name="takenNames">其他生图模型已用的名字</param>
    /// <returns>保存时为新实例，取消为 null</returns>
    public static Task<ImageModelInfo?> ShowWindow(Window owner, ImageModelInfo source,
        IReadOnlyCollection<string> takenNames)
    {
        LocalImageModelEditWindow window = new()
        {
            DataContext = new LocalImageModelEditViewData(source, takenNames, LocalImageModels.ModelRoot)
        };
        return window.ShowDialog<ImageModelInfo?>(owner);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LocalImageModelEditViewData { CanConfirm: true } viewData) return;
        Close(viewData.BuildResult());
    }
}
