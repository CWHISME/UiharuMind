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

using UiharuMind.Core.AI.Engines;

namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// 一份 llama.cpp 引擎包
/// </summary>
public class VersionInfo : EnginePackage
{
    /// <summary>
    /// 是否本机的推荐变体
    /// </summary>
    public override bool IsRecommended => LLamaCppVariants.IsRecommended(Name);
}
