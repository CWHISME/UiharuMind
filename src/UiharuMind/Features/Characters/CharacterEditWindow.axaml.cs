using Avalonia.Interactivity;
using UiharuMind.Shared.Windows;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色编辑窗。<b>只服务对话侧那两个入口</b>（对话页右栏改当前角色、会话列表右键）——
/// 那两处正在对话，跳到角色页会打断上下文，而弹窗改完就走。
/// 角色工作台里的编辑是内联的，不开这个窗。见 ADR 0014。
/// </summary>
public partial class CharacterEditWindow : UiharuWindowBase
{
    public CharacterEditWindow()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        // 草稿是一份深拷贝,直接丢掉即可——活实例上一个字也没动过
        Close();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        // 提交失败(名字空着)时不关窗:关了改动就白填了
        if (DataContext is CharacterDraft draft && !draft.TryCommit()) return;
        Close();
    }
}
