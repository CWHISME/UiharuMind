using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.WorldSettings;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Features.Characters;

namespace UiharuMind.App.Tests.Features.Characters;

/// <summary>
/// 钉住 CharacterDraft 新字段（世界设定 / 备选开场白 / 深度注入 / token 预算）与草稿本体的同步：
/// 条目内编辑必须即时反映到 Subject（提交时那份），增删条目、空深度注入、预算下限都不得走样。
/// </summary>
public class CharacterDraftSyncTests
{
    private static CharacterDraft NewDraft() =>
        CharacterDraft.ForNew(new CharacterData { CharacterName = "测试角色" }, new RecordingMessageService());

    [Fact]
    public void WorldSettingEdit_SyncsToSubjectImmediately()
    {
        CharacterDraft draft = NewDraft();
        draft.AddWorldSettingEntryCommand.Execute(null);
        draft.WorldSettingEntries[0].KeysText = "学园都市，第七学区";
        draft.WorldSettingEntries[0].Content = "中心地带";
        draft.WorldSettingEntries[0].Constant = true;
        draft.WorldSettingEntries[0].PositionIndex = 1;
        draft.WorldSettingEntries[0].Order = 3;

        WorldSettingEntry entry = Assert.Single(draft.Subject.WorldSetting.Entries);
        Assert.Equal(["学园都市", "第七学区"], entry.Keys);
        Assert.Equal("中心地带", entry.Content);
        Assert.True(entry.Constant);
        Assert.Equal(EWorldSettingPosition.AfterCharacter, entry.Position);
        Assert.Equal(3, entry.Order);
    }

    [Fact]
    public void WorldSettingAddRemove_SyncsCountAndEmptyState()
    {
        CharacterDraft draft = NewDraft();
        Assert.False(draft.HasWorldSettingEntries);

        draft.AddWorldSettingEntryCommand.Execute(null);

        Assert.True(draft.HasWorldSettingEntries);
        Assert.Single(draft.Subject.WorldSetting.Entries);

        draft.RemoveWorldSettingEntryCommand.Execute(draft.WorldSettingEntries[0]);

        Assert.Empty(draft.Subject.WorldSetting.Entries);
        Assert.False(draft.HasWorldSettingEntries);
    }

    [Fact]
    public void AlternateGreetings_SyncToSubject()
    {
        CharacterDraft draft = NewDraft();

        draft.AddAlternateGreetingCommand.Execute(null);
        draft.AlternateGreetings[0].Text = "备选一";

        Assert.Equal(["备选一"], draft.Subject.AlternateGreetings);

        draft.RemoveAlternateGreetingCommand.Execute(draft.AlternateGreetings[0]);

        Assert.Empty(draft.Subject.AlternateGreetings);
    }

    [Fact]
    public void EmptyDepthPrompt_NotPersisted()
    {
        CharacterDraft draft = NewDraft();

        // 只调角色不写字：不应凭空生成空对象
        draft.DepthPromptRoleIndex = 1;
        Assert.Null(draft.Subject.DepthPrompt);
        Assert.False(draft.HasDepthPrompt);

        draft.DepthPromptText = "记住：你在学园都市。";
        Assert.NotNull(draft.Subject.DepthPrompt);
        Assert.True(draft.HasDepthPrompt);

        // 清掉正文：回到「没有深度注入」
        draft.DepthPromptText = "";
        Assert.Null(draft.Subject.DepthPrompt);
        Assert.False(draft.HasDepthPrompt);
    }

    [Fact]
    public void TokenBudget_ClampedTo100()
    {
        CharacterDraft draft = NewDraft();

        draft.WorldSettingTokenBudget = 50;

        Assert.Equal(100, draft.Subject.WorldSetting.TokenBudget);
    }
}