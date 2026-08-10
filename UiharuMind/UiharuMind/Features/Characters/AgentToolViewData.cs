using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 智能体档的能力面板数据：工具开关 + 技能勾选。<b>直接读写角色身上那份</b>
/// <see cref="AgentToolConfig"/>，没有中间副本——它曾经是全局设置，现在唯一权威就是角色存档。
/// 保存由外层编辑窗统一做（改动只落在内存里的角色对象上）。
/// </summary>
public sealed class AgentToolViewData
{
    /// <summary>工具开关(按装配顺序排,与提示词里纪律段的次序一致)</summary>
    public ObservableCollection<AgentToolToggle> Toggles { get; } = new();

    /// <summary>技能勾选项;技能包一个都没有时为空</summary>
    public ObservableCollection<AgentSkillToggle> Skills { get; } = new();

    /// <summary>技能列表为空(界面据此显示空态文案)</summary>
    public bool HasNoSkills => Skills.Count == 0;

    /// <summary>
    /// 绑定到一份能力配置
    /// </summary>
    /// <param name="tools">角色身上那份能力配置</param>
    public AgentToolViewData(AgentToolConfig tools)
    {
        string L(string key) => LocalizationManager.Instance.GetString(key);

        Toggles.Add(new AgentToolToggle(L("AgentSettingCapFileAccess"), L("AgentGateDescFileAccess"),
            () => tools.EnableFileAccess, v => tools.EnableFileAccess = v));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapShellExecution"), L("AgentGateDescShell"),
            () => tools.EnableShellExecution, v => tools.EnableShellExecution = v));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapWebSearch"), L("AgentGateDescWebSearch"),
            () => tools.EnableWebSearch, v => tools.EnableWebSearch = v));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapVisionTool"), L("AgentGateDescVisionTool"),
            () => tools.EnableVisionTool, v => tools.EnableVisionTool = v));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapKnowledgeSearchTool"), L("AgentGateDescKnowledgeSearch"),
            () => tools.EnableKnowledgeSearchTool, v => tools.EnableKnowledgeSearchTool = v));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapSubAgent"), L("AgentGateDescSubAgent"),
            () => tools.EnableSubAgent, v => tools.EnableSubAgent = v));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapFileMemory"), L("AgentGateDescFileMemory"),
            () => tools.EnableFileMemory, v => tools.EnableFileMemory = v));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapScheduledTasks"), L("AgentGateDescScheduledTasks"),
            () => tools.EnableScheduledTasks, v => tools.EnableScheduledTasks = v));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapTodoList"), L("AgentGateDescTodoList"),
            () => tools.EnableTodoList, v => tools.EnableTodoList = v));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapAgentMode"), L("AgentGateDescAgentMode"),
            () => tools.EnableAgentMode, v => tools.EnableAgentMode = v));

        _ = LoadSkillsAsync(tools);
    }

    private async Task LoadSkillsAsync(AgentToolConfig tools)
    {
        // 技能要读盘解析,不阻塞面板构造
        List<SkillCatalogEntry> entries = await SkillCatalog.Instance.GetEntriesAsync();
        foreach (SkillCatalogEntry entry in entries.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            Skills.Add(new AgentSkillToggle(entry, tools));
        }
    }
}

/// <summary>
/// 一个工具开关。读写直穿角色那份配置，不留副本——留副本就会出现"面板显示开着、存档里是关着"。
/// </summary>
public sealed partial class AgentToolToggle : ObservableObject
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;

    /// <summary>开关名</summary>
    public string Name { get; }

    /// <summary>说明(悬停显示)</summary>
    public string Description { get; }

    /// <summary>是否启用</summary>
    public bool IsEnabled
    {
        get => _get();
        set
        {
            if (_get() == value) return;
            _set(value);
            OnPropertyChanged();
        }
    }

    public AgentToolToggle(string name, string description, Func<bool> get, Action<bool> set)
    {
        Name = name;
        Description = description;
        _get = get;
        _set = set;
    }
}

/// <summary>
/// 一个技能勾选项。配置里存的是<b>禁用</b>清单，所以勾选即"从禁用清单里移除"。
/// </summary>
public sealed partial class AgentSkillToggle : ObservableObject
{
    private readonly AgentToolConfig _tools;

    /// <summary>技能名(即目录名)</summary>
    public string Name { get; }

    /// <summary>技能描述</summary>
    public string Description { get; }

    /// <summary>框架是否接受加载(未加载的勾了也不生效,界面据此禁用)</summary>
    public bool IsLoaded { get; }

    /// <summary>是否退出了模型自选(只能点名调用)</summary>
    public bool IsUserInvokedOnly { get; }

    /// <summary>本智能体是否可用该技能</summary>
    public bool IsEnabled
    {
        get => !_tools.DisabledSkills.Any(x => string.Equals(x, Name, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (IsEnabled == value) return;
            if (value) _tools.DisabledSkills.RemoveAll(x => string.Equals(x, Name, StringComparison.OrdinalIgnoreCase));
            else _tools.DisabledSkills.Add(Name);
            OnPropertyChanged();
        }
    }

    public AgentSkillToggle(SkillCatalogEntry entry, AgentToolConfig tools)
    {
        _tools = tools;
        Name = entry.Name;
        Description = entry.Description;
        IsLoaded = entry.IsLoaded;
        IsUserInvokedOnly = !entry.IsModelInvocable;
    }
}
