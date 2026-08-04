using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Character.PromptActions;

/// <summary>
/// 一次性技能：本质是一个临时会话——不落盘、不进索引、不出现在会话列表，
/// 但走的是与普通对话完全相同的一套流程。用户选择保留时提升为正式会话。
/// </summary>
public abstract class PromptActionConvertableBase : PromptActionBase
{
    private ChatSession? _session;

    public override bool IsConvertableToChatSession => _session is { Count: > 0 };

    public override ChatSession? TryConvertToChatSession()
    {
        _session?.Persist();
        return _session;
    }

    /// <summary>
    /// 建立临时会话并跑一轮
    /// </summary>
    /// <param name="text">用户输入</param>
    /// <param name="arguments">模板参数</param>
    /// <param name="imageBytes">可选图片</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>累积文本流</returns>
    protected IAsyncEnumerable<string> RunTransientAsync(string text, Dictionary<string, object?>? arguments,
        byte[]? imageBytes = null, CancellationToken cancellationToken = default)
    {
        CharacterData character = GetCharacterData();
        _session = SessionManager.Instance.CreateTransientSession(character.CharacterId, arguments);
        _session.Description = text;
        _session.ChatModelRunningData = CurModelRunningData;

        ChatMessage input = _session.CreateMessage(ChatRole.User, text, imageBytes);
        return _session.GenerateCompletionStreaming(input, cancellationToken);
    }

    /// <summary>
    /// 本技能所用的角色
    /// </summary>
    /// <returns>角色数据</returns>
    public abstract CharacterData GetCharacterData();

    public override string ToString()
    {
        return GetCharacterData().CharacterName;
    }
}
