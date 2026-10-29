using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Features.Conversation.Pages;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 「新建会话」空态草稿的两档存取。这是页面壳（ConversationPageDataBase）持有的纯逻辑组件：
/// 会话本体还不存在时，输入文字由它暂存（NewConversationDraftConfig），打字只记内存、
/// 切走与退出前落盘、回到空态时回填、首轮建出会话后清空。
/// </summary>
public class EmptySessionDraftStoreTests
{
    private static EmptySessionDraftStore Create(out NewConversationDraftConfig config)
    {
        config = new NewConversationDraftConfig();
        return new EmptySessionDraftStore(config);
    }

    [Fact]
    public void DraftOf_StartsWithEmpty()
    {
        EmptySessionDraftStore store = Create(out _);

        Assert.Equal("", store.DraftOf(false));
        Assert.Equal("", store.DraftOf(true));
    }

    [Fact]
    public void Save_ThenDraftOf_ReturnsSavedValue_AndPersists()
    {
        EmptySessionDraftStore store = Create(out NewConversationDraftConfig config);

        store.Save(false, "你好，草稿");

        Assert.Equal("你好，草稿", store.DraftOf(false));
        Assert.Equal("你好，草稿", config.ChatDraft);
    }

    [Fact]
    public void Save_AgentAndChat_DoNotPolluteEachOther()
    {
        EmptySessionDraftStore store = Create(out NewConversationDraftConfig config);

        store.Save(false, "chat-draft");
        store.Save(true, "agent-draft");

        Assert.Equal("chat-draft", store.DraftOf(false));
        Assert.Equal("agent-draft", store.DraftOf(true));
        Assert.Equal("chat-draft", config.ChatDraft);
        Assert.Equal("agent-draft", config.AgentDraft);
    }

    [Fact]
    public void Clear_EmptiesThatSlot_AndPersistsNull()
    {
        EmptySessionDraftStore store = Create(out NewConversationDraftConfig config);
        store.Save(true, "agent-draft");

        store.Clear(true);

        Assert.Equal("", store.DraftOf(true));
        Assert.Null(config.AgentDraft);
        //另一档不受影响
        Assert.Equal("", store.DraftOf(false));
    }

    [Fact]
    public void Clear_AlreadyEmptySlot_KeepsTheOtherSlot()
    {
        EmptySessionDraftStore store = Create(out NewConversationDraftConfig config);
        store.Save(false, "chat-draft");

        store.Clear(true); //空档:什么都不该动

        Assert.Equal("chat-draft", store.DraftOf(false));
        Assert.Equal("chat-draft", config.ChatDraft);
    }

    [Fact]
    public void Ctor_ReadsPersistedDrafts_SoRestartRestores()
    {
        NewConversationDraftConfig config = new() { ChatDraft = "恢复的 chat", AgentDraft = "恢复的 agent" };

        EmptySessionDraftStore store = new(config);

        Assert.Equal("恢复的 chat", store.DraftOf(false));
        Assert.Equal("恢复的 agent", store.DraftOf(true));
    }

    /// <summary>
    /// 打字只记内存（<see cref="EmptySessionDraftStore.Set"/>），写盘要等 Flush——
    /// 退出前那一次 Flush 就是「打完字直接关窗口」的补救点
    /// </summary>
    [Fact]
    public void Set_KeepsInMemoryOnly_AndFlushWritesIt()
    {
        const string draft = "只进内存的草稿-7f3a";
        EmptySessionDraftStore store = Create(out NewConversationDraftConfig config);
        string path = AppPaths.Config.ForType(nameof(NewConversationDraftConfig));

        store.Set(false, draft);

        Assert.Equal(draft, store.DraftOf(false));
        Assert.Equal(draft, config.ChatDraft);
        Assert.DoesNotContain(draft, File.Exists(path) ? File.ReadAllText(path) : string.Empty);

        store.Flush();

        Assert.Contains(draft, File.ReadAllText(path));
    }
}