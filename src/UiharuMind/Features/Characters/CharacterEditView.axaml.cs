using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.WorldBooks;
using UiharuMind.Shared.WindowManagement;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色编辑表单本体。<b>宿主有两个</b>：角色工作台的右主区（内联，主入口）与
/// <see cref="CharacterEditWindow"/>（对话中改角色时用，不必跳页）。
/// 表单只管字段，「保存/取消」那条栏归宿主，两边的按钮位置本来就该不一样。
/// 数据上下文一律是 <see cref="CharacterDraft"/>。
/// </summary>
public partial class CharacterEditView : UserControl
{
    public CharacterEditView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 展开前给片段选择器换一份数据。片段库可能刚被别处增删，且「存为片段」要取到
    /// <b>当前</b>提示词框里的文本，所以每次展开都重新绑一份。
    /// </summary>
    private void OnSnippetPickerOpening(object? sender, EventArgs e)
    {
        if (DataContext is not CharacterDraft data) return;

        SnippetPicker.DataContext = new PromptSnippetPickerViewData(
            () => data.Template,
            text => data.InsertSnippet(text));
    }

    /// <summary>
    /// 展开前给子智能体选择器换一份数据：只列智能体，排除自己（防递归）与已挂的。
    /// 选中即追加，面板留着不关。
    /// </summary>
    private void OnSubAgentPickerOpening(object? sender, EventArgs e)
    {
        if (DataContext is not CharacterDraft data) return;

        CharacterPickerViewData picker = null!;
        picker = new CharacterPickerViewData(
            character =>
            {
                data.AddSubAgent(character);
                picker.Exclude(character.CharacterId);
            },
            filter: character => character.IsAgent,
            excludedIds: data.SubAgentAndSelfIds);
        SubAgentPicker.DataContext = picker;
    }

    // ---- 世界书挂载（共享世界设定）：选择 / 编辑 / 解除，都在弹窗里做，不在角色卡里内联改共享书 ----

    private void OnWorldBookChooseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CharacterDraft draft) return;

        WorldBookWindows.ShowSelectWindow(UIManager.GetFocusWindow(),
            name => draft.WorldSettingName = name, draft.WorldSettingName);
    }

    private void OnWorldBookEditClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CharacterDraft draft || !draft.HasWorldSettingName) return;

        WorldBookWindows.ShowEditorWindow(UIManager.GetFocusWindow(), draft.WorldSettingName);
    }

    private void OnWorldBookDetachClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CharacterDraft draft) return;

        draft.WorldSettingName = "";
    }
}
