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
            Log.Error(e);
            timeString = targetString;
        }

        return timeString;
    }

    /// <summary>
    /// 把时长写成人话（进提示词与报告正文）：不到 1 分钟说秒、不到 1 小时说分（整分不带秒）、再长说时（整点不带分）。
    /// 负数按 0 算
    /// </summary>
    /// <param name="duration">时长</param>
    /// <returns>时长文本</returns>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 1) return "0 秒";
        if (duration.TotalMinutes < 1) return $"{(int)duration.TotalSeconds} 秒";
        if (duration.TotalHours < 1)
            return duration.Seconds == 0 ? $"{(int)duration.TotalMinutes} 分" : $"{(int)duration.TotalMinutes} 分 {duration.Seconds} 秒";
        return duration.Minutes == 0 ? $"{(int)duration.TotalHours} 小时" : $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分";
    }
}