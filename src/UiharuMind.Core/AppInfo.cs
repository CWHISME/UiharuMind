/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core;

/// <summary>
/// 应用自身的标识。
/// </summary>
public static class AppInfo
{
    /// <summary>应用名</summary>
    public const string Name = "UiharuMind";

    /// <summary>版本号。取自程序集，唯一来源是 Directory.Build.props 的 &lt;Version&gt;</summary>
    public static readonly Version Version = ReadAssemblyVersion();

    // 归一成三段:程序集版本总是四段(0.1.0.0),而显示与比较沿用三段口径
    private static Version ReadAssemblyVersion()
    {
        Version? version = typeof(AppInfo).Assembly.GetName().Version;
        return version == null ? new Version(0, 0, 0) : new Version(version.Major, version.Minor, version.Build);
    }
}
