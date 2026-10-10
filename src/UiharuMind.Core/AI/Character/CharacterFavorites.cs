using System;
using System.Collections.Generic;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.AI.Character;

/// <summary>
/// 角色收藏：设置里的一个 id 列表 + 变化广播。
///
/// 刻意不放进 <see cref="CharacterData"/>：收藏是用户偏好不是卡数据——内置卡没有自己的文件，
/// 存卡上会把「收藏」与卡本身绑在一起（清空 Overrides 恢复出厂时收藏跟着没）；
/// 模型收藏（<see cref="UiharuMind.Core.AI.Runtime.Backends.ModelSettingConfig.FavoriteModels"/>）
/// 落在设置里就是先例。
/// </summary>
public static class CharacterFavorites
{
    /// <summary>
    /// 收藏变化（新增、取消）时触发。列表、选择器之类常驻视图靠它重建，
    /// 口径同 <see cref="CharacterVisibility.ShowShieldedChanged"/>
    /// </summary>
    public static event Action? Changed;

    /// <summary>
    /// 是否已收藏
    /// </summary>
    /// <param name="characterId">角色标识</param>
    /// <returns>已收藏返回 True</returns>
    public static bool IsFavorite(string characterId) =>
        SettingConfig.Current.FavoriteCharacterIds.Contains(characterId);

    /// <summary>
    /// 切换收藏状态并落盘
    /// </summary>
    /// <param name="characterId">角色标识</param>
    public static void Toggle(string characterId)
    {
        List<string> ids = SettingConfig.Current.FavoriteCharacterIds;
        if (!ids.Remove(characterId)) ids.Add(characterId);
        SettingConfig.Current.Save();
        Changed?.Invoke();
    }
}