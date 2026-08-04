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

using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Input;
using UiharuMind.Core;

namespace UiharuMind.Features.Settings;

public partial class SettingPageData : PageDataBase
{
    // public LLamaCppSettingModel LlamaSettingModel { get; set; } = new LLamaCppSettingModel();
    
    public SettingPageData()
    {
        // LlamaSettingModel.ServerSettingData = LlmManager.Instance.LLamaCppConfig;
    }
    
    public override void OnDisable()
    {
        base.OnDisable();
        // LlmManager.Instance.SaveLLamaCppConfig();
    }

    protected override Control CreateView => new SettingPage();
}