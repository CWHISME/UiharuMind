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

using System.ComponentModel;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

[DisplayName("RAG Config")]
public class LLamaCppServerRAGConfig : ConfigBase
{
    [SettingConfigDesc("pooling type for embeddings, use model default if unspecified (default: none)")]
    [SettingConfigOptions("none", "mean", "cls", "last", "rank")]
    public string Pooling { get; set; } = "none";
}