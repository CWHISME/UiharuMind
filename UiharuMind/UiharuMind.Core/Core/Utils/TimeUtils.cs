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

using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Utils;

public static class TimeUtils
{
    /// <summary>
    /// 将指定时间字符串转换为本地时间字符串
    /// 传入格式例如：
    ///     "2024-09-28T11:34:32Z"
    ///     "2024/9/28 11:34:32 +00:00"
    /// </summary>
    /// <param name="targetString">时间字符串</param>
    /// <returns>返回 yyyy-MM-dd HH:mm:ss 格式的本地时间</returns>
    public static string TimeStringToLocalTimeString(string targetString)
    {
        string timeString = "";
        try
        {
            DateTimeOffset dateTimeOffset = DateTimeOffset.Parse(targetString);

            // 转换为本地时间
            DateTime dateTime = dateTimeOffset.LocalDateTime;

            // 格式化日期
            timeString = dateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
            timeString = targetString;
        }

        return timeString;
    }
}