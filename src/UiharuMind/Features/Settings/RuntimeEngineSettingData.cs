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
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Features.Settings.Engines;
using UiharuMind.Shared.Data;
using UiharuMind.Generated;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 本地模型（文本）页：llama.cpp 引擎卡 + llama-server 的运行参数
/// </summary>
public partial class RuntimeEngineSettingData : ObservableObject
{
    private readonly IMessageService _messageService;

    public RuntimeEngineSettingData() : this(App.Services.GetRequiredService<IMessageService>())
    {
    }

    public RuntimeEngineSettingData(IMessageService messageService)
        : this(messageService, LLamaCppEngine.Shared, path => App.FilesService.OpenFolder(path))
    {
    }

    /// <param name="messageService">提示与确认</param>
    /// <param name="engine">文本引擎</param>
    /// <param name="openFolder">打开目录</param>
    public RuntimeEngineSettingData(IMessageService messageService, ILocalEngine engine, Action<string> openFolder)
    {
        _messageService = messageService;
        Engine = new EngineVersionsViewData(engine, "terminal", Loc.Text(LangKey.LLamaCppVersionDesc), messageService,
            openFolder);
    }

    /// <summary>
    /// llama.cpp 引擎卡
    /// </summary>
    public EngineVersionsViewData Engine { get; }

    /// <summary>
    /// 通用运行参数（上下文、GPU 层数…）
    /// </summary>
    public ModelRuntimeBasicSettingsData RuntimeSettings { get; } = new();

    /// <summary>
    /// llama-server 专有启动选项
    /// </summary>
    public LLamaCppServerSettingsViewData Server { get; } = new();

    [RelayCommand]
    private async Task ResetRuntimeSettings()
    {
        if (!await _messageService.ConfirmAsync(Loc.Text(LangKey.LLamaCppResetAllConfirm))) return;
        RuntimeSettings.ResetToDefaults();
        Server.ResetToDefaults();
    }
}
