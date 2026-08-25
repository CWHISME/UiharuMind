/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.Configs;

namespace UiharuMind.Features.Settings;

public partial class AgentSettingViewData : ViewModelBase
{
    private readonly SettingsWriteBack _writeBack = new(() => AgentSettingConfig.Current.Save()); //写回闸门

    //================= 常规 =================
    [ObservableProperty] private int _defaultPermissionModeIndex;
    [ObservableProperty] private string _defaultWorkspacePath = string.Empty;
    [ObservableProperty] private bool _defaultPlanMode;

    //================= 联网搜索(能力开关已下沉到角色,见 ADR 0003) =================
    /// <summary>凭据与链路状态自成一块,见 <see cref="WebSearchSettingsViewData"/></summary>
    public WebSearchSettingsViewData WebSearch { get; } = new();

    //================= 受管 Python 环境 =================
    /// <summary>
    /// 解释器探测与虚拟环境创建自成一块，见 <see cref="PythonEnvSettingsViewData"/>。
    /// 它挂在能力页而不是常规页：它讲的是"agent 能不能跑 Python"，与联网搜索凭据同性质。
    /// </summary>
    public PythonEnvSettingsViewData PythonEnv { get; } = new();

    //================= MCP =================
    /// <summary>server 列表、连接状态与编辑缓冲自成一块,见 <see cref="McpSettingsViewData"/></summary>
    public McpSettingsViewData Mcp { get; } = new();

    //================= 技能 =================
    public ObservableCollection<SkillDisplayItem> Skills { get; } = new();

    /// <summary>
    /// 技能目录的完整路径。显式摆出来是为了让第三方生成器有地方可指——
    /// 有些 MCP server（如 Unity-MCP）会按目标客户端生成 SKILL.md 落进它的技能目录，
    /// 而那类工具只认得自己硬编码的那几个客户端，认不得本项目。
    /// </summary>
    public string SkillsRootPath => SkillCatalog.Instance.SkillsRootPath;

    public AgentSettingViewData()
    {
        // 回填照常走属性:handler 会跑,但闸门关着,不会在打开设置页的瞬间把配置重写一遍
        using (_writeBack.BeginLoad())
        {
            AgentSettingConfig config = AgentSettingConfig.Current;
            DefaultPermissionModeIndex = config.DefaultPermissionModeIndex;
            DefaultWorkspacePath = config.DefaultWorkspacePath;
            DefaultPlanMode = config.DefaultPlanMode;
        }

        _ = RefreshSkillsAsync(); //技能列表要读盘解析,不阻塞构造
    }

    //================= 常规:变更即存 =================
    partial void OnDefaultPermissionModeIndexChanged(int value)
    {
        AgentSettingConfig.Current.DefaultPermissionModeIndex = value;
        _writeBack.Save();
    }

    partial void OnDefaultWorkspacePathChanged(string value)
    {
        AgentSettingConfig.Current.DefaultWorkspacePath = value;
        _writeBack.Save();
    }

    partial void OnDefaultPlanModeChanged(bool value)
    {
        AgentSettingConfig.Current.DefaultPlanMode = value;
        _writeBack.Save();
    }

    [RelayCommand]
    private async Task BrowseDefaultWorkspace()
    {
        string path = await App.FilesService.OpenSelectFolderAsync(DefaultWorkspacePath);
        if (string.IsNullOrEmpty(path)) return;

        DefaultWorkspacePath = path;
        // 这里选的目录也进最近列表:两处都是"挑一个工作目录",没理由只有会话侧记得
        AgentSettingConfig.Current.RememberWorkspace(path);
    }

    //================= 技能(SKILL.md 目录,框架规范) =================
    [RelayCommand]
    private void OpenSkillsFolder()
    {
        App.FilesService.OpenFolder(SkillCatalog.Instance.SkillsRootPath);
    }

    [RelayCommand]
    private async Task ReloadSkills()
    {
        await RefreshSkillsAsync();
    }

    /// <summary>新建一个技能模板目录并打开它,首次上手用</summary>
    [RelayCommand]
    private async Task CreateSkill()
    {
        string? directory = SkillCatalog.Instance.CreateSkillTemplate();
        if (directory == null) return;
        App.FilesService.OpenFolder(directory);
        await RefreshSkillsAsync();
    }

    private async Task RefreshSkillsAsync()
    {
        List<SkillCatalogEntry> entries = await SkillCatalog.Instance.GetEntriesAsync();

        Skills.Clear();
        foreach (SkillCatalogEntry entry in entries)
        {
            Skills.Add(new SkillDisplayItem(entry));
        }
    }
}

/// <summary>
/// 技能列表显示项（只读展示）。启停<b>不在这里</b>——技能与工具同类，属"这个智能体有什么能力"，
/// 按角色配（见 <see cref="AgentToolConfig.DisabledSkills"/> 与角色编辑页）。
/// 「模型可自选」由 SKILL.md 自己声明，属技能包的一部分而非用户偏好，同样只读。
/// </summary>
public class SkillDisplayItem
{
    /// <summary>技能名(即目录名)</summary>
    public string Name { get; }

    /// <summary>技能描述(模型自选时的匹配依据)</summary>
    public string Description { get; }

    /// <summary>是否已成功加载</summary>
    public bool IsLoaded { get; }

    /// <summary>是否退出了模型自选(只能点名调用)</summary>
    public bool IsUserInvokedOnly { get; }

    public SkillDisplayItem(SkillCatalogEntry entry)
    {
        Name = entry.Name;
        Description = entry.Description;
        IsLoaded = entry.IsLoaded;
        IsUserInvokedOnly = !entry.IsModelInvocable;
    }
}
