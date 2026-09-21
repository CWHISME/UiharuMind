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

public class SimpleStringHelper
{
    /// <summary>
    /// 格式化字节数，将字节数转换为MB
    /// </summary>
    /// <param name="bytes"></param>
    /// <returns></returns>
    public static string FormatBytes(long bytes)
    {
        return (bytes / 1024d / 1024d).ToString("F1") + " MB";
    }

    /// <summary>
    ///格式化下载信息
    /// </summary>
    /// <param name="bytesPerSecondSpeed"></param>
    /// <returns></returns>
    public static string FormatBytesWithSpeed(double bytesPerSecondSpeed)
    {
        string speedUnit = "KB/s";
        double speed = bytesPerSecondSpeed / 1024d;
        if (speed >= 1024)
        {
            speed /= 1024;
            speedUnit = "MB/s";
        }

        return speed.ToString("F1") + " " + speedUnit;
    }
}