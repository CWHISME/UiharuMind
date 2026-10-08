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
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Runtime.Backends;

public class LLamaCppSettingConfig : TConfigBase<LLamaCppSettingConfig>, IEngineVersionSelection
{
    public const string ServerExeName = "llama-server";

    /// <summary>
    /// 随应用附带的引擎目录（若有，其下 LLamaCpp 子目录里的引擎列出但不许删）
    /// </summary>
    [JsonIgnore] public string DefaultRuntimePath { get; set; } = "./InternalRuntime";

    public string? SelectedRuntimeVersion { get; set; }

    /// <summary>
    /// 引擎更新通道：关为预览版（每天都有新构建），开为正式版（只跟正式版钉住的构建）
    /// </summary>
    public bool UseStableChannel { get; set; }

    /// <summary>
    /// llama-server 专有的启动选项
    /// </summary>
    public LLamaCppServerOptions Server { get; set; } = new();
}
