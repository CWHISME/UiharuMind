using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace UiharuMind.Features.Models.ProviderPicker;

/// <summary>
/// models.dev 供应商选择器（单选、内联）。DataContext 由使用方给一个 <see cref="ProviderPickerViewData"/>。
/// </summary>
public partial class ProviderPickerView : UserControl
{
    public ProviderPickerView()
    {
        InitializeComponent();

        // 行模板必须可复用：XAML 的 DataTemplate 滚动时仍是每次新建一棵行控件树
        // （实测 20 步滚动新建 50 次、复用 0 次，见 CharacterPickerView）。
        ProviderList.ItemTemplate = new FuncDataTemplate<ProviderPickerItem>(
            static (_, _) => new ProviderPickerRow(), supportsRecycling: true);
    }
}
