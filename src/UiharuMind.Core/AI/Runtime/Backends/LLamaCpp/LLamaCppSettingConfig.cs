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

using System.Text.Json.Serialization;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Runtime.Backends;

public class LLamaCppSettingConfig : TConfigBase<LLamaCppSettingConfig>
{
    public const string ServerWinExeName = "llama-server.exe";
    public const string LookupStatsWinExeName = "llama-lookup-stats.exe";
    public const string ServerExeName = "llama-server";
    public const string LookupStatsExeName = "llama-lookup-stats";

    /// <summary>
    /// 默认运行端口
    /// </summary>
    public int DefaultPort { get; set; } = 1369;
    
    [JsonIgnore] public string DefaultRuntimePath { get; set; } = "./InternalRuntime";

    public int DefaultEmbeddedPort => DefaultPort + 1;

    public string? LLamaCppPath { get; set; }

    public string? SelectedRuntimeVersion { get; set; }
    
    public string? GetExeLookupStatsPath(string? executablePath)
    {
        if (executablePath == null) return null;
        return Path.Combine(executablePath, PlatformUtils.IsWindows ? LookupStatsWinExeName : LookupStatsExeName);
    }

    public string? GetExeServerPath(string? executablePath)
    {
        if (executablePath == null) return null;
        return Path.Combine(executablePath, PlatformUtils.IsWindows ? ServerWinExeName : ServerExeName);
    }
}
