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
        Assert.False(vm.IsSkillPickerOpen);
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
        vm.SkillCandidateAccepted += () => raised++;

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
        vm.SkillCandidates.Add(new SkillCatalogEntry { Name = "demo-skill", Description = "d" });
        vm.SkillCandidateIndex = 0;
        vm.IsSkillPickerOpen = true; //候选平时由读盘填充,这里直接摆好状态
        return vm;
    }
}
