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

using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

namespace UiharuMind.ViewModels.SettingViewData;

public class LLamaCppSettingModel : ObservableObject
{
    public LLamaCppSettingConfig LLamaCppSettingConfig => LlmManager.Instance.LLamaCppServer.Config;
}