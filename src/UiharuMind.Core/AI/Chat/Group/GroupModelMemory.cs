using System;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 建群面板的成员模型记忆：给有记忆的成员在建群时自动带出上次钉的模型，
/// 不用每次一个个手动选。读写走这里，落盘经 <see cref="GroupModelMemoryConfig"/>。
///
/// 刻意<b>只在建群场景用</b>：加人那条已按「群内既有钉选」恢复（<c>GroupAddMembersWindowModel</c>
/// 的 formerModels），会话面板钉选存会话自己——这里是「这位成员长期用哪个模型」的跨群记忆，
/// 给建群面板的预选提供来源，不给别的场景当通用偏好
/// </summary>
public static class GroupModelMemory
{
    /// <summary>记忆变化时触发。当前没有常驻视图订阅，保留给将来（如建群面板展示「上次用的模型」）</summary>
    public static event Action? Changed;

    /// <summary>
    /// 该成员角色的记忆模型名；没有返回 null
    /// </summary>
    /// <param name="characterId">角色标识</param>
    /// <returns>模型名；无记忆为 null</returns>
    public static string? Get(string characterId) =>
        GroupModelMemoryConfig.Current.ModelByCharacter.GetValueOrDefault(characterId);

    /// <summary>
    /// 记下「这位成员上次建群用的模型」。建群提交时对钉了模型的成员调用；
    /// 没钉（跟随全局）的成员不动已有记忆——他可能只是这次不想钉，不是要永久放弃
    /// </summary>
    /// <param name="characterId">角色标识</param>
    /// <param name="modelName">模型名</param>
    public static void Set(string characterId, string modelName)
    {
        if (Get(characterId) == modelName) return;
        GroupModelMemoryConfig.Current.ModelByCharacter[characterId] = modelName;
        GroupModelMemoryConfig.Current.Save();
        Changed?.Invoke();
    }
}