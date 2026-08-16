using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 点名补全开着时，回车与 Tab 必须改道成"采纳候选"。
///
/// 这条不能靠路由事件实现：Avalonia 的 <c>KeyBindings</c> 由
/// <c>KeyboardDevice.ProcessRawEvent</c> 沿视觉父链处理，时机在 KeyDown 路由事件被 raise
/// <b>之前</b>，连 Tunnel 都拦不住。所以改道点在 <c>SendMessage</c> / <c>InputExtra</c>
/// 两个命令的入口——一旦有人把它挪回 code-behind 的按键处理器，技能名还没敲全的输入就会被直接发出去。
/// </summary>
public class SkillPickerKeyRoutingTests
{
    [Fact]
    public async Task SendCommand_AcceptsCandidateInsteadOfSending_WhenPickerIsOpen()
    {
        ConversationViewModel vm = CreateViewModelWithOpenPicker("/dem");

        await vm.SendMessageCommand.ExecuteAsync(null);

        // 补全成完整技能名并留一个空格等参数,而不是把 "/dem" 发出去
        Assert.Equal("/demo-skill ", vm.InputText);
        Assert.False(vm.Palette.IsSkillPickerOpen);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void InputExtraCommand_AcceptsCandidateInsteadOfCyclingMode_WhenPickerIsOpen()
    {
        ConversationViewModel vm = CreateViewModelWithOpenPicker("/dem");
        EAgentMode modeBefore = vm.CurrentMode;

        vm.InputExtraCommand.Execute(null);

        Assert.Equal("/demo-skill ", vm.InputText);
        Assert.Equal(modeBefore, vm.CurrentMode); //Tab 没有去切 plan/execute
    }

    [Fact]
    public void AcceptSkillCandidate_DoesNothingWhenPickerIsClosed()
    {
        ConversationViewModel vm = new() { InputText = "普通消息" };

        Assert.False(vm.AcceptSkillCandidate()); //返回 false,调用方据此照常发送
        Assert.Equal("普通消息", vm.InputText);
    }

    [Fact]
    public void AcceptSkillCandidate_RaisesEventSoHostCanRestoreCaret()
    {
        ConversationViewModel vm = CreateViewModelWithOpenPicker("/dem");
        int raised = 0;
        vm.Palette.SkillCandidateAccepted += () => raised++;

        Assert.True(vm.AcceptSkillCandidate());
        Assert.Equal(1, raised);
    }

    /// <summary>
    /// 摆出"补全开着"的状态。次序要紧:写 InputText 会触发 OnInputTextChanged,
    /// 它在非 agent 会话下会 CloseSkillPicker 并清空候选,所以必须先写输入再摆候选。
    /// </summary>
    /// <param name="typed">已敲进输入框的内容</param>
    /// <returns>视图模型</returns>
    private static ConversationViewModel CreateViewModelWithOpenPicker(string typed)
    {
        ConversationViewModel vm = new() { InputText = typed };
        OpenPickerWithDemoSkill(vm.Palette);
        return vm;
    }

    /// <summary>
    /// 摆出"补全开着"的状态。候选平时由读盘填充,这里直接摆好
    /// </summary>
    /// <param name="palette">命令面板</param>
    private static void OpenPickerWithDemoSkill(CommandPaletteViewData palette)
    {
        palette.SkillCandidates.Add(new SkillCatalogEntry { Name = "demo-skill", Description = "d" });
        palette.SkillCandidateIndex = 0;
        palette.IsSkillPickerOpen = true;
    }
}

/// <summary>
/// 命令面板本身不需要一个对话视图模型就能测：它只吃「改写输入框」与「取当前角色」两个委托。
/// 这正是把它从 <see cref="ConversationViewModel"/> 里拆出来换到的东西。
/// </summary>
public class CommandPaletteViewDataTests
{
    [Fact]
    public void AcceptCandidate_WritesBackThroughTheInjectedSetter()
    {
        string written = string.Empty;
        CommandPaletteViewData palette = CreatePalette(text => written = text);
        palette.SkillCandidates.Add(new SkillCatalogEntry { Name = "demo-skill", Description = "d" });
        palette.SkillCandidateIndex = 0;
        palette.IsSkillPickerOpen = true;

        Assert.True(palette.AcceptSkillCandidate());
        Assert.Equal("/demo-skill ", written); //补全后留一个空格等参数
        Assert.False(palette.IsSkillPickerOpen);
        Assert.Empty(palette.SkillCandidates);
    }

    /// <summary>
    /// 上下移动在两端环绕。候选只有一条时任意方向都停在它自己身上——
    /// 取模写错会算出负下标,而 AcceptSkillCandidate 的下标检查会把它静默吞掉
    /// </summary>
    [Fact]
    public void MoveSelection_WrapsAroundInBothDirections()
    {
        CommandPaletteViewData palette = CreatePalette(_ => { });
        foreach (string name in new[] { "a", "b", "c" })
        {
            palette.SkillCandidates.Add(new SkillCatalogEntry { Name = name, Description = "d" });
        }

        palette.IsSkillPickerOpen = true;
        palette.SkillCandidateIndex = 0;

        palette.MoveSkillSelection(-1);
        Assert.Equal(2, palette.SkillCandidateIndex); //往上越过头部,绕到末尾

        palette.MoveSkillSelection(1);
        Assert.Equal(0, palette.SkillCandidateIndex); //往下越过末尾,绕回头部
    }

    /// <param name="setInputText">输入框写回</param>
    /// <returns>命令面板</returns>
    private static CommandPaletteViewData CreatePalette(Action<string> setInputText)
    {
        // 角色只在读技能目录时才用到,上面两条都不碰目录,给一个空角色即可
        return new CommandPaletteViewData(setInputText, () => new CharacterData());
    }
}
