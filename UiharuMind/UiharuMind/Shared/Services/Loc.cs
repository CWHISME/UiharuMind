/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Shared.Services;

/// <summary>
/// C# 侧取本地化文案的短名入口，对应 axaml 里的 <c>loc:Loc</c>。
///
/// 之所以存在：仓里原先有 <b>11 份</b>私有快捷方法（<c>L</c> / <c>Loc</c> /
/// <c>GetLocalizedText</c> / <c>GetText</c>）各写一遍同一件事，其中 7 份还是
/// <b>重新实现</b>了 <see cref="LocalizationManager.GetString"/> 的方法体
/// （<c>Lang.ResourceManager.GetString(key, CurrentCulture) ?? key</c>）——
/// 查找逻辑一改就要动 8 处。它们全部换成本类。
///
/// 直接用 <c>LocalizationManager.Instance.GetString</c> 也行，只是 120 个调用点
/// 都写全名会把行撑长，那正是当初每个 feature 各自造一份短名的原因。
/// </summary>
public static class Loc
{
    /// <summary>
    /// 取当前语言下的文案
    /// </summary>
    /// <param name="key">资源键</param>
    /// <returns>文案；键不存在时原样返回该键</returns>
    public static string Text(string key) => LocalizationManager.Instance.GetString(key);
}
