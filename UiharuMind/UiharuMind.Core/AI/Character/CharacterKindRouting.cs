namespace UiharuMind.Core.AI.Character;

/// <summary>
/// 档位的归路：一个档位走哪条装配、落在哪一页。<b>唯一定义处</b>。
///
/// 这个类的存在是为了挡住一类具体的错：两档时代（只有 Roleplay 与 Agent）到处写着
/// <c>== Roleplay</c> 或 <c>!= Roleplay</c> 当"是不是 agent"用，四档之后
/// <see cref="ECharacterKind.Tool"/> 与 <see cref="ECharacterKind.UserCard"/> 就会掉进
/// 本该只属于 agent 的那一边——症状是翻译/识图这类工具人角色被装上文件、shell、技能与整套
/// harness，或者它们的会话在两个页面都不显示。判定一律走这里，别再手写 Kind 比较。
/// </summary>
public static class CharacterKindRouting
{
    /// <summary>
    /// 是否走 agent 装配：工具、工作目录、权限档与框架 harness。
    /// </summary>
    /// <param name="kind">角色档位</param>
    /// <returns>是 agent 档则为 true</returns>
    public static bool IsAgent(this ECharacterKind kind) => kind == ECharacterKind.Agent;

    /// <summary>
    /// 是否为聊天页的档位：只渲染提示词、不开 harness 的那两档（扮演与工具人）。
    /// </summary>
    /// <param name="kind">角色档位</param>
    /// <returns>属于聊天页则为 true</returns>
    public static bool IsChat(this ECharacterKind kind) =>
        kind is ECharacterKind.Roleplay or ECharacterKind.Tool;

    /// <summary>
    /// 是否能开会话。用户卡是「我是谁」的单例，不能对话。
    /// </summary>
    /// <param name="kind">角色档位</param>
    /// <returns>可开会话则为 true</returns>
    public static bool CanStartSession(this ECharacterKind kind) => kind != ECharacterKind.UserCard;
}
