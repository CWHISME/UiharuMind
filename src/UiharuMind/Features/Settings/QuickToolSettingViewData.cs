/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Configs;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 快捷工具设置页的绑定数据：文本类与视觉类默认模型两个下拉，外加剪贴板检测间隔。
/// 模型候选与顶栏模型选择器同源（<see cref="LlmManager.GetModelList"/>），
/// 视觉下拉只列视觉模型——视觉工具不能塞给不支持多模态的模型。
/// </summary>
public partial class QuickToolSettingViewData : ViewModelBase
{
    private readonly SettingsWriteBack _writeBack = new(() => QuickToolSetting.Current.Save()); //写回闸门

    /// <summary>文本类快捷工具可选的模型列表（全部模型，与顶栏同源）</summary>
    public ObservableCollection<ModelRunningData> AvailableModels { get; } = new();

    /// <summary>视觉类快捷工具可选的模型列表（只含视觉模型）</summary>
    public ObservableCollection<ModelRunningData> AvailableVisionModels { get; } = new();

    /// <summary>文本类快捷工具的默认模型。null = 跟随顶栏当前模型。</summary>
    [ObservableProperty] private ModelRunningData? _defaultModel;

    /// <summary>视觉类快捷工具的默认模型。null = 跟随全局视觉模型自动挑选。</summary>
    [ObservableProperty] private ModelRunningData? _defaultVisionModel;

    public QuickToolSettingViewData()
    {
        LoadAvailableModels();

        // 回填照常走属性：handler 会跑，但闸门关着，不会在打开设置页的瞬间把配置重写一遍
        using (_writeBack.BeginLoad())
        {
            QuickToolSetting config = QuickToolSetting.Current;
            DefaultModel = AvailableModels.FirstOrDefault(m => m.ModelName == config.DefaultModelName);
            DefaultVisionModel = AvailableVisionModels.FirstOrDefault(m => m.ModelName == config.DefaultVisionModelName);
        }

        // 设置窗是缓存复用的，构造只跑一次；模型页后加的模型靠这个通知同步进来。
        // ViewData 与设置窗同寿命，不退订；设计态 App.ModelService 为空时跳过。
        if (App.ModelService is { } service)
            service.ModelListRefreshed += RefreshAvailableModels;
    }

    partial void OnDefaultModelChanged(ModelRunningData? value)
    {
        QuickToolSetting.Current.DefaultModelName = value?.ModelName ?? string.Empty;
        _writeBack.Save();
    }

    partial void OnDefaultVisionModelChanged(ModelRunningData? value)
    {
        QuickToolSetting.Current.DefaultVisionModelName = value?.ModelName ?? string.Empty;
        _writeBack.Save();
    }

    /// <summary>
    /// 模型列表刷新后同步两个下拉。已选对象还活着就按名找回，找不到就回到跟随模式；
    /// 全程包在回填作用域里，不会因为模型被删而把配置重写一遍。
    /// </summary>
    public void RefreshAvailableModels()
    {
        // 通知在 UI 线程上触发，这里是兜底：未来若有后台调用方也不炸绑定
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshAvailableModels);
            return;
        }

        string? textName = DefaultModel?.ModelName ?? QuickToolSetting.Current.DefaultModelName;
        string? visionName = DefaultVisionModel?.ModelName ?? QuickToolSetting.Current.DefaultVisionModelName;
        LoadAvailableModels();
        using (_writeBack.BeginLoad())
        {
            DefaultModel = string.IsNullOrEmpty(textName)
                ? null
                : AvailableModels.FirstOrDefault(m => m.ModelName == textName);
            DefaultVisionModel = string.IsNullOrEmpty(visionName)
                ? null
                : AvailableVisionModels.FirstOrDefault(m => m.ModelName == visionName);
        }
    }

    /// <summary>
    /// 从 LlmManager 加载可用模型，视觉模型单独拎出来放进视觉下拉的候选。
    /// </summary>
    private void LoadAvailableModels()
    {
        AvailableModels.Clear();
        AvailableVisionModels.Clear();
        foreach (ModelRunningData model in LlmManager.Instance.GetModelList())
        {
            AvailableModels.Add(model);
            if (model.IsVisionModel) AvailableVisionModels.Add(model);
        }
    }
}
