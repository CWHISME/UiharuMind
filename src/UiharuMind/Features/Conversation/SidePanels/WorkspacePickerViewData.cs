/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.Configs;

namespace UiharuMind.Features.Conversation.SidePanels;

/// <summary>
/// 工作目录选择器：当前目录的展示形态、最近用过的列表，以及选/切/清/揭示四个动作。
///
/// 本类<b>持有</b>工作目录这份状态（视图模型不再有 WorkspacePath），
/// 变化经 <c>onPathChanged</c> 报出去——写回会话是调用方的事，
/// 因为「什么时候该写盘」取决于它是否正在装载会话，那个判据只有它知道。
/// </summary>
public partial class WorkspacePickerViewData : ObservableObject
{
    private readonly Action<string?> _onPathChanged;

    [ObservableProperty] private string? _path;

    /// <summary>最近用过的工作目录(下拉菜单数据源,已剔除当前目录与已不存在的目录)</summary>
    public ObservableCollection<RecentWorkspaceItem> Recent { get; } = new();

    /// <param name="initialPath">初始工作目录；构造期直接落到字段，不触发变化回调</param>
    /// <param name="onPathChanged">工作目录变化</param>
    public WorkspacePickerViewData(string? initialPath, Action<string?> onPathChanged)
    {
        _onPathChanged = onPathChanged;
        _path = initialPath;
        RefreshRecent();
    }

    /// <summary>当前工作目录的目录名(卡片主行);未绑定时为空</summary>
    public string Name => string.IsNullOrEmpty(Path) ? string.Empty : WorkspaceDisplay.NameOf(Path);

    /// <summary>当前工作目录的父路径(卡片副行,已折叠 home 前缀);未绑定时为空</summary>
    public string Parent => string.IsNullOrEmpty(Path) ? string.Empty : WorkspaceDisplay.ParentOf(Path);

    partial void OnPathChanged(string? value)
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Parent));
        RefreshRecent();
        _onPathChanged(value);
    }

    /// <summary>
    /// 重建最近工作区列表。当前目录不出现在其中(切到自己是空操作),
    /// 已经不存在的目录顺手从配置里剔除——列出一个点了会失败的条目没有意义。
    /// </summary>
    public void RefreshRecent()
    {
        Recent.Clear();
        AgentSettingConfig config = AgentSettingConfig.Current;
        foreach (string path in config.RecentWorkspaces.ToList())
        {
            if (!Directory.Exists(path))
            {
                config.ForgetWorkspace(path);
                continue;
            }

            if (string.Equals(path, Path, StringComparison.Ordinal)) continue;
            Recent.Add(new RecentWorkspaceItem(path,
                new RelayCommand(() => Use(path)),
                new RelayCommand(() => Forget(path))));
        }
    }

    [RelayCommand]
    private async Task Select()
    {
        string path = await App.FilesService.OpenSelectFolderAsync(Path);
        if (!string.IsNullOrEmpty(path)) Use(path);
    }

    [RelayCommand]
    private void Clear()
    {
        Path = null;
    }

    /// <summary>在系统文件管理器里打开当前工作目录</summary>
    [RelayCommand]
    private void Reveal()
    {
        if (!string.IsNullOrEmpty(Path)) App.FilesService.OpenFolder(Path);
    }

    /// <summary>切到某个工作目录并把它记为最近使用</summary>
    /// <param name="path">工作目录</param>
    private void Use(string path)
    {
        AgentSettingConfig.Current.RememberWorkspace(path);
        Path = path;
        RefreshRecent(); //路径没变化时上面的 partial 回调不会触发,列表仍要跟上置顶顺序
    }

    private void Forget(string path)
    {
        AgentSettingConfig.Current.ForgetWorkspace(path);
        RefreshRecent();
    }
}
