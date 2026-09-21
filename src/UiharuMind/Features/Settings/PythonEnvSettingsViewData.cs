/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Execution.Python;
using UiharuMind.Core.Configs;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 受管 Python 环境这一块的设置：解释器从哪来、环境建没建好。
///
/// 它<b>不是一个能力开关</b>——本仓刻意不为跑代码另立工具，agent 用 <c>Shell</c> 调这个
/// 解释器（见 ADR 0019）。所以这里也没有"启用"勾选框，只有"建了没有"。
///
/// 建环境放在设置页的显式一步，而不是首次用到时惰性创建：装配那条路
/// （<c>AgentAssemblyPlan.Resolve</c>）是同步的、从不等网络，而建环境要起子进程、
/// 解压标准库，动辄几十秒。装配期只读 <see cref="PythonEnvironment.IsReady"/> 这一个布尔。
/// </summary>
public partial class PythonEnvSettingsViewData : ObservableObject
{
    private readonly SettingsWriteBack _writeBack = new(() => AgentSettingConfig.Current.Save()); //写回闸门

    /// <summary>宿主解释器路径（空 = 自动探测）。只在建环境那一次用得上</summary>
    [ObservableProperty] private string _interpreterPath = string.Empty;

    /// <summary>正在建或正在探测。按钮据此禁用</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>环境是否已就绪</summary>
    [ObservableProperty] private bool _isReady;

    /// <summary>给用户看的一行状态：版本号，或失败原因</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>虚拟环境所在目录（就绪后给用户一个可查看的落点）</summary>
    public string EnvironmentRoot => PythonEnvironment.Root;

    public PythonEnvSettingsViewData()
    {
        // 回填走 backing field:handler 会立刻写回配置,而此刻只是在读它
        using (_writeBack.BeginLoad())
        {
            _interpreterPath = AgentSettingConfig.Current.PythonInterpreterPath;
        }

        _isReady = PythonEnvironment.IsReady;
        _status = _isReady ? PythonEnvironment.InterpreterPath : string.Empty;
    }

    partial void OnInterpreterPathChanged(string value)
    {
        AgentSettingConfig.Current.PythonInterpreterPath = value;
        _writeBack.Save();
    }

    /// <summary>
    /// 探测宿主解释器，把结果显示成一行状态。不改变环境。
    /// </summary>
    [RelayCommand]
    private async Task ProbeAsync()
    {
        IsBusy = true;
        try
        {
            PythonProbeResult result = await PythonEnvironment.ProbeHostAsync();
            Status = result.Ok ? $"{result.InterpreterPath}（{result.Message}）" : result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 创建虚拟环境。进度直接刷在状态行上——建环境是几十秒的事，一动不动的界面像卡死。
    /// </summary>
    [RelayCommand]
    private async Task CreateAsync()
    {
        IsBusy = true;
        try
        {
            PythonProbeResult result = await PythonEnvironment.CreateAsync(line => Status = line);
            Status = result.Ok ? $"{result.InterpreterPath}（{result.Message}）" : result.Message;
            IsReady = PythonEnvironment.IsReady;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 删除虚拟环境。装进去的包一并没了，换解释器重建时用。
    /// </summary>
    [RelayCommand]
    private void Remove()
    {
        PythonEnvironment.Remove();
        IsReady = PythonEnvironment.IsReady;
        Status = string.Empty;
    }
}
