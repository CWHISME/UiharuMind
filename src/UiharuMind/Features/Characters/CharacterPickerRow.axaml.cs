using Avalonia.Controls;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色选择器里的一行：头像、名字与描述、收藏星标。
/// 由 <see cref="CharacterPickerView"/> 的可复用模板创建——滚动时同一实例换 DataContext 复用，
/// 不再每次重建控件树。
/// </summary>
public partial class CharacterPickerRow : UserControl
{
    public CharacterPickerRow()
    {
        InitializeComponent();
    }
}
