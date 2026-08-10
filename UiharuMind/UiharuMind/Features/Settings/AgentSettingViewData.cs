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
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.Configs;

namespace UiharuMind.Features.Settings;

public partial class AgentSettingViewData : ViewModelBase
{
    //================= 常规 =================
    [ObservableProperty] private int _defaultPermissionModeIndex;
    [ObservableProperty] private string _defaultWorkspacePath = string.Empty;
    [ObservableProperty] private bool _defaultPlanMode;

    //================= 搜索凭据(能力开关已下沉到角色,见 ADR 0003) =================
    [ObservableProperty] private string _tavilyApiKey = string.Empty;
    [ObservableProperty] private string _braveSearchApiKey = string.Empty;

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
        _tavilyApiKey = config.TavilyApiKey;
        _braveSearchApiKey = config.BraveSearchApiKey;

        RefreshServers();
        _ = RefreshSkillsAsync(); //技能列表要读盘解析,不阻塞构造
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

    //================= 能力门控:开关/提示词的写入在 AgentGateItem 内完成 =================
    partial void OnTavilyApiKeyChanged(string value)
    {
        AgentSettingConfig.Current.TavilyApiKey = value;
        AgentSettingConfig.Current.Save();
    }

    partial void OnBraveSearchApiKeyChanged(string value)
    {
        AgentSettingConfig.Current.BraveSearchApiKey = value;
        AgentSettingConfig.Current.Save();
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
        // 强制刷新工具缓存以建立连接
        await McpManager.Instance.RefreshAsync();
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
