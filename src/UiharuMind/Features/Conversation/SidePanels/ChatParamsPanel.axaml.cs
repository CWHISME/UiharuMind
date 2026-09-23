using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Features.Conversation.SessionList;

namespace UiharuMind.Features.Conversation.SidePanels;

public partial class ChatParamsPanel : UserControl
{
    public ChatParamsPanel()
    {
        InitializeComponent();
    }
}

/// <summary>
/// 会话详情栏的对话参数(角色的执行设置)。
///
/// <b>草稿语义</b>：面板绑的是 <see cref="Draft"/>，拨动控件只改草稿，不碰活角色；
/// 点保存才盖到角色身上并落盘，点取消则丢弃。没点保存就发下一轮，用的仍是旧参数。
/// 之前是直接绑活角色、开轮时自动落盘——调了个采样参数就会在 Overrides/ 下给内置卡
/// 落一个全量快照，从此屏蔽内置卡的一切后续更新，故改掉。
/// </summary>
public partial class ChatParamsViewData : ObservableObject
{
    [ObservableProperty] private CharacterData _character = null!;

    [ObservableProperty] private ChatPromptExecutionSettings _draft = new();

    /// <summary>切到某会话:参数面板改编辑这个会话所属角色的执行设置</summary>
    /// <param name="session">会话列表条目</param>
    public void SetSession(SessionListItem session)
    {
        Character = session.Session.CharacterData;
        Draft = CloneSettings(Character.Config.ExecutionSettings);
    }

    /// <summary>保存:草稿盖到活角色身上并落盘。显式动作，内置卡也会写覆盖文件</summary>
    [RelayCommand]
    private void Save()
    {
        if (Character == null) return;
        ApplyTo(Draft, Character.Config.ExecutionSettings);
        Character.Config.ExecutionSettings.IsDirty = false;
        Character.Save();
        Draft = CloneSettings(Character.Config.ExecutionSettings);
    }

    /// <summary>取消:丢弃草稿，回到活角色当前的值</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (Character == null) return;
        Draft = CloneSettings(Character.Config.ExecutionSettings);
    }

    private static ChatPromptExecutionSettings CloneSettings(ChatPromptExecutionSettings source)
    {
        return SaveUtility.LoadFromString<ChatPromptExecutionSettings>(SaveUtility.SaveToString(source));
    }

    /// <summary>
    /// 把草稿的值逐字段搬进活实例。就地赋值而不换实例：装配读的就是活角色身上的这份。
    /// <b>新增采样参数时这里要跟着加</b>，漏了会静默存不进。
    /// </summary>
    private static void ApplyTo(ChatPromptExecutionSettings source, ChatPromptExecutionSettings target)
    {
        target.OmitSamplingParams = source.OmitSamplingParams;
        target.Temperature = source.Temperature;
        target.TopP = source.TopP;
        target.PresencePenalty = source.PresencePenalty;
        target.FrequencyPenalty = source.FrequencyPenalty;
    }
}
