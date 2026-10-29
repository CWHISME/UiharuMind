using UiharuMind.Core.Configs;

namespace UiharuMind.Features.Conversation.Pages;

/// <summary>
/// 「新建会话」空态输入框草稿的两档存取（普通对话 / 智能体）。
/// 空态会话本体尚不存在，输入文字挂不到 <c>ChatSession.ComposerDraft</c>，
/// 由页面壳在切走前收进这里、回到空态时回填，首轮建出会话（草稿已被消费）后清空。
/// 持久化放在 <see cref="NewConversationDraftConfig"/>，与普通会话草稿同级：
/// 打字只记内存（<see cref="Set"/>），落盘交给切走与应用退出两处（<see cref="Flush"/>）。
/// </summary>
public sealed class EmptySessionDraftStore
{
    private readonly NewConversationDraftConfig _config;
    private string _agentDraft = string.Empty; // 智能体档「新建」空态草稿
    private string _chatDraft = string.Empty; // 普通对话档「新建」空态草稿
    private bool _dirty; // 内存里有还没写盘的改动

    /// <param name="config">持久化的配置对象（测试可传独立实例）</param>
    public EmptySessionDraftStore(NewConversationDraftConfig config)
    {
        _config = config;
        _agentDraft = config.AgentDraft ?? string.Empty;
        _chatDraft = config.ChatDraft ?? string.Empty;
    }

    /// <summary>取某档草稿（isAgentForm 为 true 取智能体档）</summary>
    public string DraftOf(bool isAgentForm) => isAgentForm ? _agentDraft : _chatDraft;

    /// <summary>
    /// 只记进内存、不碰磁盘：空态每敲一个字就整份写盘不值当，写盘交给切走与退出（见 <see cref="Flush"/>）。
    /// 与内存里那份相同就什么都不做
    /// </summary>
    /// <param name="isAgentForm">true 为智能体档</param>
    /// <param name="draft">当前输入内容</param>
    public void Set(bool isAgentForm, string draft)
    {
        if (isAgentForm)
        {
            if (_agentDraft == draft) return;
            _agentDraft = draft;
            _config.AgentDraft = draft;
        }
        else
        {
            if (_chatDraft == draft) return;
            _chatDraft = draft;
            _config.ChatDraft = draft;
        }

        _dirty = true;
    }

    /// <summary>
    /// 保存某档草稿并落盘（切走前调）。没变化不落盘（对标 <see cref="UiharuMind.Core.AI.Chat.ChatSession.SaveDraft"/>
    /// 的 diff 口径）
    /// </summary>
    /// <param name="isAgentForm">true 为智能体档</param>
    /// <param name="draft">当前输入内容</param>
    public void Save(bool isAgentForm, string draft)
    {
        Set(isAgentForm, draft);
        Flush();
    }

    /// <summary>把脏了的那份写盘（切走、应用退出前调）。没改过就什么都不做</summary>
    public void Flush()
    {
        if (!_dirty) return;
        _dirty = false;
        _config.Save();
    }

    /// <summary>清空某档草稿并立刻落盘（首轮建出会话后调用）</summary>
    public void Clear(bool isAgentForm)
    {
        if (isAgentForm)
        {
            if (_agentDraft.Length == 0) return;
            _agentDraft = string.Empty;
            _config.AgentDraft = null;
        }
        else
        {
            if (_chatDraft.Length == 0) return;
            _chatDraft = string.Empty;
            _config.ChatDraft = null;
        }

        _dirty = false;
        _config.Save();
    }
}
