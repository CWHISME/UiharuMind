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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Features.Models;
using UiharuMind.Generated;

namespace UiharuMind.Features.Settings;

public partial class AgentSettingViewData : ViewModelBase
{
    private readonly SettingsWriteBack _writeBack = new(() => AgentSettingConfig.Current.Save()); //写回闸门

    //================= 常规 =================
    [ObservableProperty] private int _defaultPermissionModeIndex;

    /// <summary>
    /// 可选模型列表(与顶栏模型选择器同源)。通用子代理从中选模型，只有一行，并在常规页里。
    /// </summary>
    public ObservableCollection<ModelRunningData> AvailableModels { get; } = new();

    /// <summary>
    /// 当前选中的通用子代理模型。null = 回退到主代理模型。
    /// </summary>
    [ObservableProperty] private ModelRunningData? _generalSubAgentModel;

    /// <summary>
    /// 当前选中的脑模型（ADR 0082）。null = 回退到主代理模型。
    /// </summary>
    [ObservableProperty] private ModelRunningData? _brainSubAgentModel;

    /// <summary>
    /// 当前选中的探索档模型（CreateAgent 置 readOnly 派出）。null = 回退到主代理模型。
    /// </summary>
    [ObservableProperty] private ModelRunningData? _explorerSubAgentModel;

    /// <summary>
    /// 通用子代理默认使用的角色卡。null = 内置「通用子代理」基座卡（无人格，只拿任务书）。
    /// <b>只作用于不点名的通用档</b>，脑与探索档不受影响。
    /// </summary>
    [ObservableProperty] private CharacterData? _defaultSubAgentCharacter;

    /// <summary>记忆清单字数上限（ADR 0076）。数值框绑的是 decimal，存回去取整</summary>
    [ObservableProperty] private decimal _memoryIndexBudget;

    /// <summary>记忆清单字数上限可设范围，与 Core 夹值同一处</summary>
    public decimal MemoryIndexBudgetMin => MemoryIndex.MinListBudget;

    /// <summary>记忆清单字数上限可设范围，与 Core 夹值同一处</summary>
    public decimal MemoryIndexBudgetMax => MemoryIndex.MaxListBudget;

    // 探索型子代理模型（CreateAgent 置 readOnly 派出的探索档）曾随 ADR 0044 退役、
    // 选择器删了；现随探索档复活（SubAgentProfile.Explorer）恢复为常规页一行，与脑/通用同款。

    //================= 联网搜索(能力开关已下沉到角色,见 ADR 0003) =================
    /// <summary>凭据与链路状态自成一块,见 <see cref="WebSearchSettingsViewData"/></summary>
    public WebSearchSettingsViewData WebSearch { get; } = new();

    /// <summary>群聊离席的保险丝与唤醒节奏（ADR 0055）</summary>
    public GroupAwaySettingsViewData Away { get; } = new();

    //================= 反馈 =================
    /// <summary>最近一次改动的反馈文本（如「已保存」），空串不显示</summary>
    [ObservableProperty] private string _statusText = string.Empty;

    // 「值≠出厂值」才显示单项恢复默认按钮（SettingsRow 可见性绑定用）；出厂值引用 AgentSettingConfig 常量
    public bool IsDefaultPermissionModeNotDefault => DefaultPermissionModeIndex != AgentSettingConfig.FactoryDefaultPermissionModeIndex;
    public bool IsGeneralSubAgentModelNotDefault => GeneralSubAgentModel is not null; // null = 回退主代理，出厂态
    public bool IsBrainSubAgentModelNotDefault => BrainSubAgentModel is not null; // 同上
    public bool IsExplorerSubAgentModelNotDefault => ExplorerSubAgentModel is not null; // 同上
    public bool IsDefaultSubAgentCharacterNotDefault => DefaultSubAgentCharacter is not null; // null = 内置基座卡，出厂态
    public bool IsMemoryIndexBudgetNotDefault => MemoryIndexBudget != AgentSettingConfig.FactoryDefaultMemoryIndexBudget;

    //================= 模型 Shell =================
    /// <summary>
    /// 模型用 shell 工具执行命令时实际调哪个解释器，自成一块，见 <see cref="ShellSettingsViewData"/>。
    /// 挂能力页：与受管 Python 环境同性质（"模型用什么跑命令"），且 Python 环境建立在它之上。
    /// </summary>
    public ShellSettingsViewData ShellEnv { get; } = new();

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
    /// <summary>按「包 → 分类」两级分好组的技能列表</summary>
    public ObservableCollection<SkillGroupItem> SkillGroups { get; } = new();

    /// <summary>
    /// 是否把技能清单发给模型。关闭后模型侧看不到技能广告列表(框架的 load_skill 等
    /// 三个工具不挂),只剩点名调用可达,外加自建同名 load_skill 加载点名技能正文里
    /// 引用的被动技能——本地小窗口模型省固定开销用。全局开关,ADR 0003 例外。
    /// </summary>
    [ObservableProperty] private bool _modelSkillsEnabled;

    /// <summary>一个技能都没扫到</summary>
    [ObservableProperty] private bool _hasNoSkills;

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
            MemoryIndexBudget = Math.Clamp(config.MemoryIndexBudget, MemoryIndex.MinListBudget, MemoryIndex.MaxListBudget);
            ModelSkillsEnabled = config.ModelSkillsEnabled;
            LoadAvailableModels();
            // 判据与派活侧同一处（SubAgentDefaultCharacter）：卡被删/被屏蔽时这里直接回填 null，
            // 界面与实跑不会一个显示旧卡、一个悄悄降级
            DefaultSubAgentCharacter = SubAgentDefaultCharacter.Resolve(config);
        }
        // 设置窗是缓存复用的，构造只跑一次；模型页后加的模型靠这个通知同步进来。
        // ViewData 与设置窗同寿命，不退订；设计态 App.ModelService 为空时跳过。
        if (App.ModelService is { } service)
            service.ModelListRefreshed += RefreshAvailableModels;
        _ = RefreshSkillsAsync(); //技能列表要读盘解析,不阻塞构造
        // 开发者模式运行中开关会增删 uiharu-dev（ADR 0061）：内置不经过扫盘缓存，重查重画即可，不必 Invalidate。
        // 与设置窗同寿命，不退订（同 ModelListRefreshed）
        DeveloperMode.Changed += RefreshSkillsOnDeveloperModeChanged;
    }

    //================= 常规:变更即存 =================
    partial void OnDefaultPermissionModeIndexChanged(int value)
    {
        AgentSettingConfig.Current.DefaultPermissionModeIndex = value;
        _writeBack.Save();
        OnPropertyChanged(nameof(IsDefaultPermissionModeNotDefault));
        // 回填时 IsLoading 为真，不弹反馈；只有用户真的改了才提示
        if (!_writeBack.IsLoading) StatusText = Loc.Text(LangKey.ShortcutSavedTips);
    }

    //================= 常规:记忆清单字数上限,变更即存,下一份新挂的清单起生效 =================
    partial void OnMemoryIndexBudgetChanged(decimal value)
    {
        AgentSettingConfig.Current.MemoryIndexBudget = (int)value;
        _writeBack.Save();
        OnPropertyChanged(nameof(IsMemoryIndexBudgetNotDefault));
        if (!_writeBack.IsLoading) StatusText = Loc.Text(LangKey.ShortcutSavedTips);
    }

    [RelayCommand]
    private void ResetMemoryIndexBudget() => MemoryIndexBudget = AgentSettingConfig.FactoryDefaultMemoryIndexBudget;

    //================= 技能:变更即存 =================
    partial void OnModelSkillsEnabledChanged(bool value)
    {
        AgentSettingConfig.Current.ModelSkillsEnabled = value;
        _writeBack.Save();
    }

    //================= 常规:通用子代理模型,变更即存 =================
    partial void OnGeneralSubAgentModelChanged(ModelRunningData? value)
    {
        AgentSettingConfig.Current.GeneralSubAgentModelName = value?.ModelName ?? string.Empty;
        _writeBack.Save();
        OnPropertyChanged(nameof(IsGeneralSubAgentModelNotDefault));
        if (!_writeBack.IsLoading) StatusText = Loc.Text(LangKey.ShortcutSavedTips);
    }

    //================= 常规:脑模型,变更即存（ADR 0082） =================
    partial void OnBrainSubAgentModelChanged(ModelRunningData? value)
    {
        AgentSettingConfig.Current.BrainSubAgentModelName = value?.ModelName ?? string.Empty;
        _writeBack.Save();
        OnPropertyChanged(nameof(IsBrainSubAgentModelNotDefault));
        if (!_writeBack.IsLoading) StatusText = Loc.Text(LangKey.ShortcutSavedTips);
    }

    //================= 常规:探索档模型,变更即存 =================
    partial void OnExplorerSubAgentModelChanged(ModelRunningData? value)
    {
        AgentSettingConfig.Current.ExplorerSubAgentModelName = value?.ModelName ?? string.Empty;
        _writeBack.Save();
        OnPropertyChanged(nameof(IsExplorerSubAgentModelNotDefault));
        if (!_writeBack.IsLoading) StatusText = Loc.Text(LangKey.ShortcutSavedTips);
    }

    //================= 常规:子代理默认角色卡,变更即存,之后新派出的通用子代理起生效 =================
    partial void OnDefaultSubAgentCharacterChanged(CharacterData? value)
    {
        AgentSettingConfig.Current.DefaultSubAgentCharacterId = value?.CharacterId ?? string.Empty;
        _writeBack.Save();
        OnPropertyChanged(nameof(IsDefaultSubAgentCharacterNotDefault));
        OnPropertyChanged(nameof(DefaultSubAgentCharacterText));
        if (!_writeBack.IsLoading) StatusText = Loc.Text(LangKey.ShortcutSavedTips);
    }

    /// <summary>
    /// 按钮上显示谁。<b>未设置时不是空白</b>：空白读不出「我关掉了它」还是「它没生效」，
    /// 这里明说回退到内置卡。
    /// </summary>
    public string DefaultSubAgentCharacterText =>
        DefaultSubAgentCharacter is { CharacterName.Length: > 0 } card
            ? card.CharacterName
            : Loc.Text(LangKey.AgentSettingSubAgentCharacterEmpty);

    /// <summary>
    /// 选择器选中一张卡。null 走 <see cref="ResetDefaultSubAgentCharacter"/>，不从这里进。
    /// </summary>
    /// <param name="character">选中的角色卡</param>
    public void PickDefaultSubAgentCharacter(CharacterData character) => DefaultSubAgentCharacter = character;

    /// <summary>
    /// 展开选择器前重新核对一遍：角色可能刚被删除、或锁回屏蔽状态，而设置页是缓存复用的，
    /// 不做这一步就会一直顶着一张已经不存在的卡。判据直接用 Core 那一份
    /// （<see cref="SubAgentDefaultCharacter.Resolve()"/>），因此界面显示与派活实际行为不会分叉。
    /// 顺带刷新按钮文字：角色改名后绑定的是旧字符串。
    /// </summary>
    public void RevalidateDefaultSubAgentCharacter()
    {
        if (DefaultSubAgentCharacter != null && SubAgentDefaultCharacter.Resolve() is null)
            DefaultSubAgentCharacter = null; // 卡没了：配置与界面一起回退，handler 负责写回

        OnPropertyChanged(nameof(DefaultSubAgentCharacterText));
    }

    /// <summary>
    /// 模型列表刷新后同步下拉。已选还活着就按名找回，找不到就回到跟随主代理模式；
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

        string? selectedName = GeneralSubAgentModel?.ModelName
            ?? AgentSettingConfig.Current.GeneralSubAgentModelName;
        string? brainName = BrainSubAgentModel?.ModelName
            ?? AgentSettingConfig.Current.BrainSubAgentModelName;
        string? explorerName = ExplorerSubAgentModel?.ModelName
            ?? AgentSettingConfig.Current.ExplorerSubAgentModelName;
        RebuildAvailableModels();
        using (_writeBack.BeginLoad())
        {
            GeneralSubAgentModel = string.IsNullOrEmpty(selectedName)
                ? null
                : AvailableModels.FirstOrDefault(m => m.ModelName == selectedName);
            BrainSubAgentModel = string.IsNullOrEmpty(brainName)
                ? null
                : AvailableModels.FirstOrDefault(m => m.ModelName == brainName);
            ExplorerSubAgentModel = string.IsNullOrEmpty(explorerName)
                ? null
                : AvailableModels.FirstOrDefault(m => m.ModelName == explorerName);
        }
    }

    /// <summary>
    /// 从 LlmManager 加载可用模型列表,并回填当前选中的通用子代理模型与脑模型。
    /// 调用方需包在回填作用域里（构造里已包），否则回填赋值会误落盘。
    /// </summary>
    private void LoadAvailableModels()
    {
        RebuildAvailableModels();

        AgentSettingConfig config = AgentSettingConfig.Current;
        if (!string.IsNullOrWhiteSpace(config.GeneralSubAgentModelName))
        {
            GeneralSubAgentModel = AvailableModels.FirstOrDefault(m => m.ModelName == config.GeneralSubAgentModelName);
        }
        if (!string.IsNullOrWhiteSpace(config.BrainSubAgentModelName))
        {
            BrainSubAgentModel = AvailableModels.FirstOrDefault(m => m.ModelName == config.BrainSubAgentModelName);
        }
        if (!string.IsNullOrWhiteSpace(config.ExplorerSubAgentModelName))
        {
            ExplorerSubAgentModel = AvailableModels.FirstOrDefault(m => m.ModelName == config.ExplorerSubAgentModelName);
        }
    }

    /// <summary>
    /// 只重建候选列表，不碰已选。刷新路径用它，避免中间态的回填赋值误落盘。
    /// </summary>
    private void RebuildAvailableModels()
    {
        AvailableModels.Clear();
        foreach (ModelRunningData model in LlmManager.Instance.GetModelList())
            AvailableModels.Add(model);
    }

    //================= 单项恢复默认 =================
    // 出厂值引用 AgentSettingConfig 常量，不手抄数字；走属性赋值，handler 统一保存+反馈
    [RelayCommand]
    private void ResetDefaultPermissionMode()
    {
        DefaultPermissionModeIndex = AgentSettingConfig.FactoryDefaultPermissionModeIndex;
    }

    [RelayCommand]
    private void ResetGeneralSubAgentModel()
    {
        GeneralSubAgentModel = null; // 回退到主代理模型
    }

    [RelayCommand]
    private void ResetBrainSubAgentModel()
    {
        BrainSubAgentModel = null; // 回退到主代理模型
    }

    [RelayCommand]
    private void ResetExplorerSubAgentModel()
    {
        ExplorerSubAgentModel = null; // 回退到主代理模型
    }

    [RelayCommand]
    private void ResetDefaultSubAgentCharacter()
    {
        DefaultSubAgentCharacter = null; // 回退到内置「通用子代理」基座卡
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
        SkillCatalog.Instance.Invalidate(); //扫描结果是缓存的,不作废就只是把同一份重画一遍
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

        SkillGroups.Clear();
        foreach (SkillGroupItem group in SkillGrouping.Build(entries)) SkillGroups.Add(group);
        HasNoSkills = SkillGroups.Count == 0;
    }

    private void RefreshSkillsOnDeveloperModeChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) _ = RefreshSkillsAsync();
        else Dispatcher.UIThread.Post(() => _ = RefreshSkillsAsync());
    }
}
