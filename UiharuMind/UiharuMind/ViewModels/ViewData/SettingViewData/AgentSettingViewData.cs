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
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Agent.Mcp;
using UiharuMind.Core.AI.Agent.Skills;
using UiharuMind.Core.Configs;
using UiharuMind.Services;
using UiharuMind.ViewModels;

namespace UiharuMind.ViewModels.ViewData.SettingViewData;

public partial class AgentSettingViewData : ViewModelBase
{
    //================= 常规 =================
    [ObservableProperty] private int _defaultPermissionModeIndex;
    [ObservableProperty] private string _defaultWorkspacePath = string.Empty;
    [ObservableProperty] private bool _defaultPlanMode;

    //================= 能力开关 =================
    [ObservableProperty] private bool _enableFileAccess = true;
    [ObservableProperty] private bool _enableShellExecution = true;
    [ObservableProperty] private bool _enableWebSearch = true;
    [ObservableProperty] private bool _enableMemory = true;
    [ObservableProperty] private bool _enableTodo = true;

    // 子 agent 档案已废除:子 agent 就是一个普通角色,由角色编辑器维护,
    // 由 agent 角色的 MountAgents(委托挂载)决定启用哪些。

    //================= MCP =================
    public ObservableCollection<McpServerConfig> McpServers { get; } = new();
    [ObservableProperty] private McpServerConfig? _selectedServer;
    [ObservableProperty] private int _serverTransportIndex;
    [ObservableProperty] private string _serverStatusText = string.Empty;

    //================= 技能 =================
    public ObservableCollection<SkillDisplayItem> Skills { get; } = new();

    public AgentSettingViewData()
    {
        AgentSettingConfig config = AgentSettingConfig.Current;
        _defaultPermissionModeIndex = config.DefaultPermissionModeIndex;
        _defaultWorkspacePath = config.DefaultWorkspacePath;
        _defaultPlanMode = config.DefaultPlanMode;
        _enableFileAccess = config.EnableFileAccess;
        _enableShellExecution = config.EnableShellExecution;
        _enableWebSearch = config.EnableWebSearch;
        _enableMemory = config.EnableMemory;
        _enableTodo = config.EnableTodo;

        RefreshServers();
        RefreshSkills();
    }

    //================= 常规:变更即存 =================
    partial void OnDefaultPermissionModeIndexChanged(int value)
    {
        AgentSettingConfig.Current.DefaultPermissionModeIndex = value;
        AgentSettingConfig.Current.Save();
    }

    partial void OnDefaultWorkspacePathChanged(string value)
    {
        AgentSettingConfig.Current.DefaultWorkspacePath = value;
        AgentSettingConfig.Current.Save();
    }

    partial void OnDefaultPlanModeChanged(bool value)
    {
        AgentSettingConfig.Current.DefaultPlanMode = value;
        AgentSettingConfig.Current.Save();
    }

    //================= 能力开关:变更即存 =================
    partial void OnEnableFileAccessChanged(bool value)
    {
        AgentSettingConfig.Current.EnableFileAccess = value;
        AgentSettingConfig.Current.Save();
    }

    partial void OnEnableShellExecutionChanged(bool value)
    {
        AgentSettingConfig.Current.EnableShellExecution = value;
        AgentSettingConfig.Current.Save();
    }

    partial void OnEnableWebSearchChanged(bool value)
    {
        AgentSettingConfig.Current.EnableWebSearch = value;
        AgentSettingConfig.Current.Save();
    }

    partial void OnEnableMemoryChanged(bool value)
    {
        AgentSettingConfig.Current.EnableMemory = value;
        AgentSettingConfig.Current.Save();
    }

    partial void OnEnableTodoChanged(bool value)
    {
        AgentSettingConfig.Current.EnableTodo = value;
        AgentSettingConfig.Current.Save();
    }

    [RelayCommand]
    private async Task BrowseDefaultWorkspace()
    {
        string path = await App.FilesService.OpenSelectFolderAsync(DefaultWorkspacePath);
        if (!string.IsNullOrEmpty(path)) DefaultWorkspacePath = path;
    }

    //================= MCP =================
    partial void OnSelectedServerChanged(McpServerConfig? value)
    {
        ServerTransportIndex = value == null ? 0 : (int)value.TransportType;
        RefreshServerStatus();
    }

    [RelayCommand]
    private void NewServer()
    {
        McpServerConfig server = new()
        {
            Name = $"server-{DateTime.Now:HHmmss}",
            IsEnabled = false,
        };
        McpManager.Instance.SaveServer(server);
        RefreshServers();
        SelectedServer = McpServers.FirstOrDefault(x => x.Name == server.Name);
    }

    [RelayCommand]
    private void SaveServer()
    {
        if (SelectedServer == null) return;
        SelectedServer.TransportType = (EMcpTransportType)Math.Clamp(ServerTransportIndex, 0, 1);
        McpManager.Instance.SaveServer(SelectedServer);
        RefreshServers(SelectedServer.Name);
        RefreshServerStatus();
    }

    [RelayCommand]
    private void DeleteServer()
    {
        if (SelectedServer == null) return;
        McpManager.Instance.DeleteServer(SelectedServer.Name);
        RefreshServers();
    }

    [RelayCommand]
    private async Task TestServer()
    {
        if (SelectedServer == null) return;
        ServerStatusText = LocalizationManager.Instance.GetString("AgentMcpStateConnecting");
        // 触发一次工具枚举以建立连接
        await McpManager.Instance.GetToolsAsync();
        RefreshServerStatus();
    }

    private void RefreshServers(string? keepSelectedName = null)
    {
        McpServers.Clear();
        foreach (McpServerConfig server in McpManager.Instance.GetServers())
        {
            McpServers.Add(server);
        }

        SelectedServer = McpServers.FirstOrDefault(x => x.Name == keepSelectedName) ?? McpServers.FirstOrDefault();
    }

    private void RefreshServerStatus()
    {
        if (SelectedServer == null)
        {
            ServerStatusText = string.Empty;
            return;
        }

        var (state, toolCount) = McpManager.Instance.GetServerState(SelectedServer.Name);
        string stateText = LocalizationManager.Instance.GetString($"AgentMcpState{state}");
        ServerStatusText = toolCount > 0 ? $"{stateText} · {toolCount} tools" : stateText;
    }

    //================= 技能(SKILL.md 目录,框架规范) =================
    [RelayCommand]
    private void OpenSkillsFolder()
    {
        App.FilesService.OpenFolder(SkillCatalog.Instance.SkillsRootPath);
    }

    [RelayCommand]
    private void ReloadSkills()
    {
        RefreshSkills();
    }

    private void RefreshSkills()
    {
        Skills.Clear();
        var disabled = AgentSettingConfig.Current.DisabledSkills;
        foreach (SkillCatalogEntry entry in SkillCatalog.Instance.GetEntries())
        {
            bool isEnabled = !disabled.Any(x => string.Equals(x, entry.Name, StringComparison.OrdinalIgnoreCase));
            Skills.Add(new SkillDisplayItem(entry, isEnabled));
        }
    }
}

/// <summary>
/// 技能列表显示项(启停即存)
/// </summary>
public partial class SkillDisplayItem : ObservableObject
{
    public string Name { get; }
    public string Description { get; }

    [ObservableProperty] private bool _isEnabled;

    public SkillDisplayItem(SkillCatalogEntry entry, bool isEnabled)
    {
        Name = entry.Name;
        Description = entry.Description;
        _isEnabled = isEnabled;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        SkillCatalog.Instance.SetSkillEnabled(Name, value);
    }
}
