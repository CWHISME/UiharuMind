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
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Configs;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 子代理模型的多选槽：一档（通用 / 探索 / 评审 / 整理）的已选模型与落盘。
///
/// 各档除了「存配置里的哪一列」之外完全同构，所以行为只写一遍——换档只是换一对读写委托。
/// 用户勾选的先后就是派活时轮到它们的先后
/// （<c>SubAgentModelRotation</c>，ADR 0111）。
///
/// <b>已选集合实例恒定</b>：界面把它当 <c>MultiComboBox.SelectedItems</c> 直接占用——
/// 控件自己往这个集合里增删，不是推一个新集合回来。所以回填只能清空再重填，换实例等于把绑定断开。
/// <b>配置里那一列相反，每次写都换实例</b>：那个列表还要被后台线程的派活读，
/// 原地增删会让读方拿到空名单或直接越界。
/// </summary>
public sealed partial class SubAgentModelSlot : ObservableObject
{
    private readonly SettingsWriteBack _writeBack; //写回闸门
    private readonly Func<AgentSettingConfig> _config; //配置来源（取用时不缓存，与设置页其余处同一口径）
    private readonly Func<AgentSettingConfig, List<string>> _read; //读配置里这一档的模型名名单
    private readonly Action<AgentSettingConfig, List<string>> _write; //写回那一列（换实例，不原地改）
    private readonly Action _saved; //改完的反馈（回填期间不触发）
    private bool _filling; //正在清空重填：集合的增删不是用户输入

    /// <param name="candidates">候选模型列表，与界面共用一个实例</param>
    /// <param name="writeBack">写回闸门，回填期间不落盘</param>
    /// <param name="config">配置来源</param>
    /// <param name="read">读配置里这一档的模型名名单</param>
    /// <param name="write">写回那一列。由调用方赋值，槽不原地改配置里的 List</param>
    /// <param name="saved">用户改动落盘后的反馈（如「已保存」）</param>
    public SubAgentModelSlot(ObservableCollection<ModelRunningData> candidates, SettingsWriteBack writeBack,
        Func<AgentSettingConfig> config, Func<AgentSettingConfig, List<string>> read,
        Action<AgentSettingConfig, List<string>> write, Action saved)
    {
        Candidates = candidates;
        _writeBack = writeBack;
        _config = config;
        _read = read;
        _write = write;
        _saved = saved;
        Selected.CollectionChanged += OnSelectedChanged;
    }

    /// <summary>候选模型（与顶栏模型选择器同源）。选择器控件从它取 ItemsSource</summary>
    public ObservableCollection<ModelRunningData> Candidates { get; }

    /// <summary>已选模型，顺序即轮换顺序</summary>
    public ObservableCollection<ModelRunningData> Selected { get; } = new();

    /// <summary>
    /// 「值≠出厂值」才显示单项恢复默认按钮。<b>看配置、不看已选</b>：配置里可能只剩「模型库已经没有」
    /// 的名字（删了模型/换了机器），界面上一个也显示不出来，但那仍然不是出厂态——
    /// 看已选的话按钮不亮，这些看不见的名字就永远没入口清掉，而它们仍会参与派活时的轮换。
    /// </summary>
    public bool IsNotDefault => _read(_config()).Count > 0;

    /// <summary>
    /// 按配置回填（读档那一次）。不需要包在回填闸门里：<see cref="Fill"/> 整段自己屏蔽
    /// （<c>_filling</c>），集合的增删不会当成用户输入。
    /// </summary>
    public void Load() => Fill(_read(_config()));

    /// <summary>
    /// 候选列表重建后按名找回：已选还活着就还在，顺序也不动。
    /// 同时把配置里<b>候选此刻没有</b>的模型补回去——模型被停用/被删时界面显示不出来，
    /// 但那不代表用户要把它从配置里删掉，模型回来就该自动再生效（与改动前单值那套一致）。
    /// </summary>
    public void Refresh() =>
        Fill(Selected.Select(model => model.ModelName).Concat(_read(_config())));

    /// <summary>
    /// 回退到主代理模型：已选清空，<b>连同配置里那些界面上看不见的名字一起清掉</b>——
    /// 它们在候选里已经没有，不给这条命令挖个口子就只能手改 JSON。
    /// </summary>
    [RelayCommand]
    private void Reset()
    {
        // 先把幽灵清出配置，再清已选：后者的落盘会走 ComposeNames，
        // 而它会把「配置里有、候选里没有」的名字补回去
        AgentSettingConfig config = _config();
        _write(config, new List<string>());
        Selected.Clear(); // 触发一次落盘与可见性重算（此时 ComposeNames 已无幽灵可补）
    }

    private void Fill(IEnumerable<string> names)
    {
        List<ModelRunningData> wanted = new();
        foreach (string name in names)
        {
            ModelRunningData? model = Candidates.FirstOrDefault(m => m.ModelName == name);
            if (model != null && !wanted.Contains(model)) wanted.Add(model);
        }

        _filling = true;
        try
        {
            Selected.Clear();
            foreach (ModelRunningData model in wanted) Selected.Add(model);
        }
        finally
        {
            _filling = false;
        }

        // 重建后的可见性要重算：配置里只剩「模型已不在候选里」那类时按钮也得能亮
        OnPropertyChanged(nameof(IsNotDefault));
    }

    private void OnSelectedChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_filling) return;

        // 回填与候选重建期间的增删不是用户意图：控件重建 ItemsSource 时会自己推一轮集合变更
        if (_writeBack.IsLoading)
        {
            OnPropertyChanged(nameof(IsNotDefault));
            return;
        }

        // 换实例而不是原地改：派活常在后台线程上读这一列，而 List 的原地增删
        // 会让读方拿到空名单或直接越界（连 ToArray 都不是原子的）。发布出去的列表不再变
        AgentSettingConfig config = _config();
        _write(config, ComposeNames(_read(config)));
        _writeBack.Save();
        // 可见性跟着配置走，所以要在写完之后再说（取消最后一个勾选时按钮该减下去）
        OnPropertyChanged(nameof(IsNotDefault));
        _saved();
    }

    /// <summary>
    /// 落盘的名字：先界面顺序，再补上「候选里此刻没有」的那些（理由见 <see cref="Refresh"/>）。
    /// 候选里<b>有</b>却不在已选里的不进配置——那就是用户取消勾选的意思。
    /// </summary>
    /// <param name="current">配置里此刻的名单</param>
    private List<string> ComposeNames(IReadOnlyCollection<string> current)
    {
        List<string> names = Selected.Select(model => model.ModelName).ToList();
        foreach (string name in current)
        {
            if (names.Contains(name)) continue;
            if (Candidates.Any(model => model.ModelName == name)) continue;
            names.Add(name);
        }

        return names;
    }
}
