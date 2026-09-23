namespace UiharuMind.Core.AI.Character;

/// <summary>
/// 身份的归路：一个角色走哪条装配、归到哪一类会话。<b>唯一定义处</b>。
///
/// 是不是智能体直读 <see cref="CharacterData.IsAgent"/>；这里只定义由两个标记<b>组合</b>出来的判据。
/// 这个类挡的是一类具体的错：到处手写 <c>!IsAgent</c> 当「是不是普通角色」用，
/// 用户卡就会掉进普通角色那一边——出现在普通对话列表、被当成能开会话的角色。
/// </summary>
public static class CharacterKindRouting
{
    /// <summary>是否为普通角色（不开 harness、也不是用户卡）。</summary>
    /// <param name="character">角色</param>
    /// <returns>普通角色则为 true</returns>
    public static bool IsChat(this CharacterData character) => !character.IsAgent && !character.IsUserCard;

    /// <summary>
    /// 是否能开会话。用户卡是「我是谁」的单例，不能对话。
    /// </summary>
    /// <param name="character">角色</param>
    /// <returns>可开会话则为 true</returns>
    public static bool CanStartSession(this CharacterData character) => !character.IsUserCard;
}
