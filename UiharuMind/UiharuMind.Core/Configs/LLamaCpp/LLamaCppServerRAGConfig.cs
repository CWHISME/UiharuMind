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
    [SettingConfigDesc("pooling type for embeddings")]
    [SettingConfigOptions("none", "mean", "cls", "last", "rank")]
    [DefaultValue("none")]
    //标记 DefaultValue 后，默认取 { get; set; } = "cls"; 赋值参数
    public string Pooling { get; set; } = "cls";
}