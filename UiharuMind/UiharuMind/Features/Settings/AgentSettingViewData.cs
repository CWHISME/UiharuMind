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
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.Configs;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 能力门控条目：左列 toggle+名称，右栏描述/可编辑提示词/专属设置。
/// 变更即写配置并保存；提示词只存"覆盖"（与默认相同或为空 = 用默认），
/// 因此重置=清空覆盖，默认措辞升级时未覆盖的用户自动跟随。
/// </summary>
public sealed partial class AgentGateItem : ObservableObject
{
    private readonly Func<bool> _getEnabled; //现读现写 Current,配置对象可能被重载替换
    private readonly Action<bool> _setEnabled;
    private readonly Func<string>? _getPromptOverride;
    private readonly Action<string>? _setPromptOverride;

    /// <summary>显示名</summary>
    public string Name { get; }

    /// <summary>详细描述</summary>
    public string Description { get; }

    /// <summary>默认提示词(无提示词的门控为空串)</summary>
    public string DefaultPrompt { get; }

    /// <summary>是否带可编辑提示词</summary>
    public bool HasPrompt => _setPromptOverride != null;

    /// <summary>是否为网络搜索门控(右栏追加 API key 面板)</summary>
    public bool IsWebSearchGate { get; init; }

    public AgentGateItem(string name, string description,
        Func<bool> getEnabled, Action<bool> setEnabled,
        string defaultPrompt = "",
        Func<string>? getPromptOverride = null, Action<string>? setPromptOverride = null)
    {
        Name = name;
        Description = description;
        DefaultPrompt = defaultPrompt;
        _getEnabled = getEnabled;
        _setEnabled = setEnabled;
        _getPromptOverride = getPromptOverride;
        _setPromptOverride = setPromptOverride;
    }

    /// <summary>开关(变更即存)</summary>
    public bool IsEnabled
    {
        get => _getEnabled();
        set
        {
            if (_getEnabled() == value) return;
            _setEnabled(value);
            AgentSettingConfig.Current.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>生效中的提示词(覆盖为空时显示默认;编辑即存)</summary>
    public string PromptText
    {
        get
        {
            string overrideText = _getPromptOverride?.Invoke() ?? string.Empty;
            return string.IsNullOrWhiteSpace(overrideText) ? DefaultPrompt : overrideText;
        }
        set
        {
            if (_setPromptOverride == null || value == PromptText) return;
            _setPromptOverride(value.Trim() == DefaultPrompt ? string.Empty : value);
            AgentSettingConfig.Current.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 重置为默认提示词(清空覆盖)
    /// </summary>
    [RelayCommand]
    private void ResetPrompt()
    {
        if (_setPromptOverride == null) return;
        _setPromptOverride(string.Empty);
        AgentSettingConfig.Current.Save();
        OnPropertyChanged(nameof(PromptText));
    }
}

public partial class AgentSettingViewData : ViewModelBase
{
    //================= 常规 =================
    [ObservableProperty] private int _defaultPermissionModeIndex;
    [ObservableProperty] private string _defaultWorkspacePath = string.Empty;
    [ObservableProperty] private bool _defaultPlanMode;

    //================= 能力门控(左列表右详情) =================
    public ObservableCollection<AgentGateItem> Gates { get; } = new();
    [ObservableProperty] private AgentGateItem? _selectedGate;
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

        BuildGates();
        RefreshServers();
        RefreshSkills();
    }

    /// <summary>
    /// 构建门控列表。开关与提示词的读写都现取 <see cref="AgentSettingConfig.Current"/>,
    /// 不缓存配置对象引用。
    /// </summary>
    private void BuildGates()
    {
        string L(string key) => LocalizationManager.Instance.GetString(key);

        Gates.Add(new AgentGateItem(L("AgentSettingCapFileAccess"), L("AgentGateDescFileAccess"),
            () => AgentSettingConfig.Current.EnableFileAccess,
            v => AgentSettingConfig.Current.EnableFileAccess = v,
            AgentToolPrompts.FileAccessDefault,
            () => AgentSettingConfig.Current.FileAccessPrompt,
            v => AgentSettingConfig.Current.FileAccessPrompt = v));

        Gates.Add(new AgentGateItem(L("AgentSettingCapShellExecution"), L("AgentGateDescShell"),
            () => AgentSettingConfig.Current.EnableShellExecution,
            v => AgentSettingConfig.Current.EnableShellExecution = v));

        Gates.Add(new AgentGateItem(L("AgentSettingCapWebSearch"), L("AgentGateDescWebSearch"),
            () => AgentSettingConfig.Current.EnableWebSearch,
            v => AgentSettingConfig.Current.EnableWebSearch = v)
        {
            IsWebSearchGate = true,
        });

        Gates.Add(new AgentGateItem(L("AgentSettingCapVisionTool"), L("AgentGateDescVisionTool"),
            () => AgentSettingConfig.Current.EnableVisionTool,
            v => AgentSettingConfig.Current.EnableVisionTool = v,
            AgentToolPrompts.VisionToolDefault,
            () => AgentSettingConfig.Current.VisionToolPrompt,
            v => AgentSettingConfig.Current.VisionToolPrompt = v));

        Gates.Add(new AgentGateItem(L("AgentSettingCapMemorySearchTool"), L("AgentGateDescMemorySearch"),
            () => AgentSettingConfig.Current.EnableMemorySearchTool,
            v => AgentSettingConfig.Current.EnableMemorySearchTool = v,
            AgentToolPrompts.MemorySearchDefault,
            () => AgentSettingConfig.Current.MemorySearchPrompt,
            v => AgentSettingConfig.Current.MemorySearchPrompt = v));

        Gates.Add(new AgentGateItem(L("AgentSettingCapAgentNotes"), L("AgentGateDescAgentNotes"),
            () => AgentSettingConfig.Current.EnableAgentNotes,
            v => AgentSettingConfig.Current.EnableAgentNotes = v));

        Gates.Add(new AgentGateItem(L("AgentSettingCapScheduledTasks"), L("AgentGateDescScheduledTasks"),
            () => AgentSettingConfig.Current.EnableScheduledTasks,
            v => AgentSettingConfig.Current.EnableScheduledTasks = v));

        Gates.Add(new AgentGateItem(L("AgentSettingCapTodoList"), L("AgentGateDescTodoList"),
            () => AgentSettingConfig.Current.EnableTodoList,
            v => AgentSettingConfig.Current.EnableTodoList = v));

        Gates.Add(new AgentGateItem(L("AgentSettingCapAgentMode"), L("AgentGateDescAgentMode"),
            () => AgentSettingConfig.Current.EnableAgentMode,
            v => AgentSettingConfig.Current.EnableAgentMode = v));

        SelectedGate = Gates.FirstOrDefault();
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
