/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 放置所有不好直接分类的工具方法
/// </summary>
public static class GameUtils
{
    /// <summary>
    /// 复制对象
    /// </summary>
    /// <param name="obj"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T Copy<T>(T obj) where T : new()
    {
        if (obj == null) return new T();
        return SaveUtility.LoadFromString<T>(SaveUtility.SaveToString(obj));
    }
}