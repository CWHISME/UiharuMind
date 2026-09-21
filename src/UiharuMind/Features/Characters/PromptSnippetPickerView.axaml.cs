using Avalonia.Controls;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 提示词片段选择器（内联）。DataContext 由使用方给一个 <see cref="PromptSnippetPickerViewData"/>。
/// </summary>
public partial class PromptSnippetPickerView : UserControl
{
    public PromptSnippetPickerView()
    {
        InitializeComponent();
    }
}
