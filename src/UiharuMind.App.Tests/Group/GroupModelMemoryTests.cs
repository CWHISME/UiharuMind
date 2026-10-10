using System.Linq;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.Configs;
using UiharuMind.Features.Conversation.Group;
using UiharuMind.Features.Conversation.SidePanels;

namespace UiharuMind.App.Tests.Group;

/// <summary>
/// 建群面板的成员模型记忆：记「这位成员上次建群钉的模型」，建群时自动带出。
/// 刻意只服务建群——加人按群内既有钉选恢复、会话面板钉选存会话自己，
/// 这里的记忆就是给建群面板预选提供来源的
/// </summary>
[Collection(CharacterLibraryCollection.Name)]
public class GroupModelMemoryTests
{
    private const string TempAgentId = "test-mem-agent";
    private const string TempChatId = "test-mem-chat";

    private static CharacterData AddTemp(string id, bool isAgent, string name)
    {
        CharacterData card = new()
        {
            CharacterId = id,
            IsAgent = isAgent,
            CharacterName = name,
        };
        card.NormalizeParams();
        Assert.True(CharacterManager.Instance.TryAddNewCharacterData(card));
        return card;
    }

    private static void Cleanup()
    {
        CharacterManager.Instance.DeleteCharacterData(TempAgentId);
        CharacterManager.Instance.DeleteCharacterData(TempChatId);
        GroupModelMemoryConfig.Current.ModelByCharacter.Clear();
        GroupModelMemoryConfig.Current.Save();
    }

    [Fact]
    public void Get_Set_StoresAndBroadcasts()
    {
        int changes = 0;
        void OnChanged() => changes++;
        GroupModelMemory.Changed += OnChanged;
        try
        {
            Assert.Null(GroupModelMemory.Get(TempChatId));
            GroupModelMemory.Set(TempChatId, "gpt-test");
            Assert.Equal("gpt-test", GroupModelMemory.Get(TempChatId));

            GroupModelMemory.Set(TempChatId, "gpt-test"); //同值不重复广播
            Assert.Equal(1, changes);

            GroupModelMemory.Set(TempChatId, "other");
            Assert.Equal(2, changes);
        }
        finally
        {
            GroupModelMemory.Changed -= OnChanged;
            Cleanup();
        }
    }

    /// <summary>建群弹窗：有记忆的成员带出记忆模型（在清单里时）；无模型清单环境回落跟随全局</summary>
    [Fact]
    public void CreateWindow_PrefillsPinnedFromMemory()
    {
        AddTemp(TempChatId, false, "试卡普通");
        GroupModelMemory.Set(TempChatId, "gpt-test");
        try
        {
            GroupCreateWindowModel model = new(true, null); //纯逻辑环境无模型清单 → 记忆落空，保持跟随全局
            Assert.Null(model.Picker.Candidates.First(c => c.Data.CharacterId == TempChatId)
                .SelectedModelOption?.ModelName);

            // 补上模型清单再预填同一来源：命中后带出记忆模型
            model.Picker.ModelOptions.Add(new SessionModelOption { ModelName = "gpt-test", DisplayName = "gpt-test" });
            model.Picker.PrefillFrom(GroupModelMemory.Get);
            Assert.Equal("gpt-test", model.Picker.Candidates.First(c => c.Data.CharacterId == TempChatId)
                .SelectedModelOption?.ModelName);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>记忆里的模型已删/改名：落回跟随全局，不显示缺失条</summary>
    [Fact]
    public void Prefill_UnknownModelFallsBackToFollowGlobal()
    {
        AddTemp(TempChatId, false, "试卡普通");
        GroupModelMemory.Set(TempChatId, "ghost-model");
        try
        {
            GroupCreateWindowModel model = new(true, null);
            model.Picker.PrefillFrom(GroupModelMemory.Get);

            Assert.Null(model.Picker.Candidates.First(c => c.Data.CharacterId == TempChatId)
                .SelectedModelOption?.ModelName);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>提交时记下钉了模型的成员；跟随全局的成员不记、也不清已有记忆</summary>
    [Fact]
    public void RememberPickedModels_StoresPinned_SkipsFollowGlobal()
    {
        AddTemp(TempAgentId, true, "试卡智能体");
        AddTemp(TempChatId, false, "试卡普通");
        GroupModelMemory.Set(TempChatId, "keep-old"); //已有记忆，这次跟随全局不应被清掉
        try
        {
            GroupCreateWindowModel model = new(true, null);
            model.Picker.ModelOptions.Add(new SessionModelOption { ModelName = "gpt-test", DisplayName = "gpt-test" });

            model.Picker.Pick(TempAgentId, "gpt-test"); //钉了模型
            model.Picker.Pick(TempChatId); //跟随全局

            model.RememberPickedModels();

            Assert.Equal("gpt-test", GroupModelMemory.Get(TempAgentId));
            Assert.Equal("keep-old", GroupModelMemory.Get(TempChatId));
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>加人弹窗的 Picker 不预填：记忆只给建群用</summary>
    [Fact]
    public void AddMembers_DoesNotPrefillFromMemory()
    {
        AddTemp(TempChatId, false, "试卡普通");
        GroupModelMemory.Set(TempChatId, "gpt-test");
        try
        {
            GroupCandidatePicker picker = new();
            Assert.Null(picker.Candidates.First(c => c.Data.CharacterId == TempChatId)
                .SelectedModelOption?.ModelName);
        }
        finally
        {
            Cleanup();
        }
    }
}