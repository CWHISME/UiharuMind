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
        // 走 source-gen（AOT 卡点 #2）：调用方 T 全在有界类型图里（MemoryData/WorldSetting/AutoClickSession）
        return SaveUtility.LoadFromString<T>(SaveUtility.SaveToString<T>(obj), SaveUtility.SaveJsonOptions);
    }

    /// <summary>
    /// 字节转字符串显示
    /// </summary>
    /// <param name="bytes"></param>
    /// <returns></returns>
    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }
}