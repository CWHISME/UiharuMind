using System.Collections.Generic;
using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.Configs;

/// <summary>
/// 建群面板的成员模型记忆：成员角色 id → 模型名。
/// 只在建群场景读写（见 <see cref="UiharuMind.Core.AI.Chat.Group.GroupModelMemory"/>）；
/// 加人按群内既有钉选恢复、会话面板钉选存会话自己，都不来这里
/// </summary>
public class GroupModelMemoryConfig : TConfigBase<GroupModelMemoryConfig>
{
    public Dictionary<string, string> ModelByCharacter { get; set; } = new();
}