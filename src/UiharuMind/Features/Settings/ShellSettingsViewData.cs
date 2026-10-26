/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.Configs;
using UiharuMind.Generated;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 「模型 Shell」设置块：模型用 shell 工具执行命令时实际调哪个解释器。
///
/// 只存一个字符串（<see cref="AgentSettingConfig.ShellBinaryPath"/>，空 = 跟随系统默认），
/// 预设下拉是快捷填写路径的 UX，不是另一个维度。生效点在会话装配（<see cref="AgentAssemblyPlan"/>），
/// 改完要新装配/重装配的会话才用上，这里在描述里点一句。
/// </summary>
public partial class ShellSettingsViewData : ObservableObject
{
    private readonly SettingsWriteBack _writeBack = new(() => AgentSettingConfig.Current.Save());

    /// <summary>下拉显示项：自动检测 + 平台预设 + 自定义路径</summary>
    public ObservableCollection<ShellPresetOption> Options { get; } = new();

    /// <summary>当前选中的显示项；null 仅在列表尚未回填时出现</summary>
    [ObservableProperty] private ShellPresetOption? _selectedOption;

    /// <summary>「自定义路径」选中时出现的输入框内容；只在自定义态写回配置</summary>
    [ObservableProperty] private string _customPath = string.Empty;

    /// <summary>一行状态：当前生效的 shell，或「已保存」反馈</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>选的是自定义路径项时才显示路径输入框</summary>
    public bool IsCustomPathVisible => SelectedOption?.IsCustom == true;

    /// <summary>「值≠出厂值」才显示恢复默认：配置了覆盖路径（非空）即非出厂态</summary>
    public bool IsShellBinaryPathNotDefault => !string.IsNullOrWhiteSpace(AgentSettingConfig.Current.ShellBinaryPath);

    public ShellSettingsViewData()
    {
        // 显示项：自动 → 平台探测到的预设 → 自定义。探测不中的预设不列出，走自定义即可。
        Options.Add(new ShellPresetOption("auto", Loc.Text(LangKey.AgentSettingShellAuto), string.Empty, isCustom: false));
        foreach (ShellPresetCatalog.ShellPreset preset in ShellPresetCatalog.DetectForCurrentPlatform())
            Options.Add(new ShellPresetOption(preset.Id, preset.Name, preset.BinaryPath, isCustom: false));
        Options.Add(new ShellPresetOption("custom", Loc.Text(LangKey.AgentSettingShellCustom), string.Empty, isCustom: true));

        string configured = AgentSettingConfig.Current.ShellBinaryPath;
        using (_writeBack.BeginLoad())
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                SelectedOption = Options[0];
            }
            else
            {
                ShellPresetOption? preset = Options.FirstOrDefault(o => o.BinaryPath == configured);
                if (preset is not null)
                {
                    SelectedOption = preset;
                }
                else
                {
                    SelectedOption = Options[^1]; //自定义
                    CustomPath = configured;
                }
            }
        }

        // 打开设置页就告诉用户当前实际生效的是哪个 shell（改了之后点「探测」再确认）
        Status = Loc.Text(LangKey.AgentSettingShellEffective,
            ShellPresetCatalog.GetEffectiveShellBinary(configured));
    }

    partial void OnSelectedOptionChanged(ShellPresetOption? value)
    {
        OnPropertyChanged(nameof(IsCustomPathVisible));
        if (value is null) return;
        if (value.IsCustom)
        {
            // 切到自定义且已有内容：立即落盘；空着则保持原配置，等用户填
            if (!string.IsNullOrWhiteSpace(CustomPath)) Write(CustomPath);
            return;
        }
        Write(value.BinaryPath);
    }

    partial void OnCustomPathChanged(string value)
    {
        // 只有自定义态才写：预设态下输入框不可见，不会产生误写
        if (SelectedOption?.IsCustom != true) return;
        Write(value);
    }

    /// <summary>把覆盖路径写回配置并落盘；空串 = 回到跟随系统默认</summary>
    private void Write(string path)
    {
        AgentSettingConfig.Current.ShellBinaryPath = path;
        _writeBack.Save();
        OnPropertyChanged(nameof(IsShellBinaryPathNotDefault));
        if (!_writeBack.IsLoading) Status = Loc.Text(LangKey.ShortcutSavedTips);
    }

    /// <summary>按当前配置实例化一次执行器，报出实际生效的 shell 二进制</summary>
    [RelayCommand]
    private void Probe()
    {
        Status = Loc.Text(LangKey.AgentSettingShellEffective,
            ShellPresetCatalog.GetEffectiveShellBinary(AgentSettingConfig.Current.ShellBinaryPath));
    }

    /// <summary>单项恢复默认：回到「自动检测」并清空覆盖路径</summary>
    [RelayCommand]
    private void ResetShellBinary()
    {
        SelectedOption = Options[0];
    }
}

/// <summary>下拉里的一行：auto / 平台预设 / custom。选中即把 <see cref="BinaryPath"/> 写进配置</summary>
public sealed class ShellPresetOption
{
    public ShellPresetOption(string id, string name, string binaryPath, bool isCustom)
    {
        Id = id;
        Name = name;
        BinaryPath = binaryPath;
        IsCustom = isCustom;
    }

    public string Id { get; }
    public string Name { get; }
    public string BinaryPath { get; }
    public bool IsCustom { get; }
}
