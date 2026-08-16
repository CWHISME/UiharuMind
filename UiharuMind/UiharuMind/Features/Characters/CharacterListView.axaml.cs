using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色工作台左栏。筛选子菜单在代码里填：选项来自
/// <see cref="CharacterListViewData.FilterTags"/>（档位是枚举派生的，不是写死的几条）。
/// </summary>
public partial class CharacterListView : UserControl
{
    public CharacterListView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        BuildFilterMenu();
    }

    private void BuildFilterMenu()
    {
        if (DataContext is not CharacterListViewData data) return;

        FilterMenuItem.Items.Clear();
        for (int i = 0; i < data.FilterTags.Length; i++)
        {
            int index = i;
            MenuItem item = new()
            {
                Header = data.FilterTags[i],
                ToggleType = MenuItemToggleType.Radio,
                GroupName = nameof(CharacterListViewData.FilterTagIndex),
                IsChecked = index == data.FilterTagIndex,
            };
            item.Click += (_, _) => data.FilterTagIndex = index;
            FilterMenuItem.Items.Add(item);
        }
    }
}
