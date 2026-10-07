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

    
    [JsonIgnore] public string DefaultRuntimePath { get; set; } = "./InternalRuntime";


    public string? LLamaCppPath { get; set; }

    public string? SelectedRuntimeVersion { get; set; }

    /// <summary>
    /// 引擎更新通道：关为预览版（每天都有新构建），开为正式版（只跟正式版钉住的构建）
    /// </summary>
    public bool UseStableChannel { get; set; }

    /// <summary>
    /// llama-server 专有的启动选项
    /// </summary>
    public LLamaCppServerOptions Server { get; set; } = new();
    
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
