/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色工作台：<b>左边选，右边改</b>。
///
/// 编辑器是内联的（不再是「列表 → 预览卡 → 又一个窗」三层），因此这里得替编辑窗
/// 兜住原本由「关窗」隐含的那件事：草稿没提交就要走开时问一句。见 ADR 0014。
/// </summary>
public partial class HomePageData : PageDataBase
{
    private readonly IMessageService _messageService;

    protected override Control CreateView => new HomePage();

    /// <summary>左栏宽度。它是导航列表，不是画廊，所以默认窄</summary>
    [ObservableProperty] private float _listPaneWidth = 280;

    [ObservableProperty] private CharacterListViewData _characterListViewData;

    /// <summary>右主区那份草稿；没有选中角色时为 null</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasEditor))] [NotifyPropertyChangedFor(nameof(IsEditingNew))]
    private CharacterDraft? _editor;

    private bool _isDrivingSelection; //选中项是我们自己在改(回滚、撤占位项),不是用户在切角色

    /// <summary>右主区是否有东西可编辑</summary>
    public bool HasEditor => Editor != null;

    public HomePageData() : this(App.Services.GetRequiredService<IMessageService>())
    {
    }

    public HomePageData(IMessageService messageService)
    {
        _messageService = messageService;
        _characterListViewData = new CharacterListViewData();
        _characterListViewData.NewCharacterRequested = NewCharacterAsync;
        _editor = CreateEditorFor(_characterListViewData.SelectedCharacter);
    }

    public override void OnEnable()
    {
        base.OnEnable();
        CharacterListViewData.EventOnSelectedCharacterChanged += OnSelectedCharacterChanged;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        CharacterListViewData.EventOnSelectedCharacterChanged -= OnSelectedCharacterChanged;
    }

    /// <summary>
    /// 新建角色：先定档，右主区当场变成一份新草稿，左栏顶上一条占位项。
    /// <b>先定档再进表单</b>——三个档位的表单面孔差得太多，进去再翻旗标会让人对着
    /// 一堆当前档用不上的字段发愣。
    /// </summary>
    /// <param name="kind">档位</param>
    private async Task NewCharacterAsync(ECharacterKind kind)
    {
        if (!await ConfirmLeaveEditorAsync()) return;

        CharacterData seed = new() { Kind = kind };
        // 智能体预填工作循环那一节:它是弱模型最依赖的几条,而现在它归角色提示词管(ADR 0004),
        // 不预填就等于新建出来的智能体默认少了这段。用户可以照常改写或删掉
        if (seed.Kind.IsAgent()) seed.Template = AgentToolPrompts.AgentWorkLoop;

        // 这两步一起改选中项(撤掉上一个占位项、顶上新的),中间那几次跳动不该被当成用户在切角色
        DrivingSelection(() =>
        {
            CharacterListViewData.BeginPending(seed);
            Editor = CharacterDraft.ForNew(seed);
        });
    }

    /// <summary>提交当前草稿</summary>
    [RelayCommand]
    private void SaveEditor()
    {
        Editor?.TryCommit();
        OnPropertyChanged(nameof(IsEditingNew));
    }

    /// <summary>
    /// 丢弃当前草稿。已入库的角色重开一份干净草稿（「重置」）；
    /// 还没入库的连占位项一起撤掉（「取消」）。改过东西的先问一句。
    ///
    /// 按钮<b>不按脏与否置灰</b>：能力面板与参数面板都是直写草稿对象、不经草稿的属性通知，
    /// 那个信号做不成可靠的实时量；而点击时现算的脏检查不论改动来自哪里都准。
    /// </summary>
    [RelayCommand]
    private async Task CancelEditor()
    {
        if (Editor == null) return;
        if (Editor.IsDirty && !await _messageService.ConfirmAsync(Lang.CharacterEditorDiscardTips)) return;

        LeaveNewCharacter();
        Editor = CreateEditorFor(CharacterListViewData.SelectedCharacter);
        OnPropertyChanged(nameof(IsEditingNew));
    }

    /// <summary>
    /// 离开一个还没入库的新角色：连占位项一起撤掉。
    ///
    /// 没入库的角色是<b>编辑器的一个临时状态</b>，不是列表里能导航回去的条目——
    /// 留着的话，切走再切回来会拿它开一份「编辑已有角色」的草稿，
    /// 于是一个还没存过的角色上会出现「开始对话」。
    /// </summary>
    private void LeaveNewCharacter()
    {
        if (Editor?.IsNew != true) return;
        DrivingSelection(CharacterListViewData.CancelPending);
    }

    /// <summary>正在编辑的是个还没入库的新角色（顶栏据此换按钮文案、藏掉复制/删除）</summary>
    public bool IsEditingNew => Editor?.IsNew == true;

    private async void OnSelectedCharacterChanged(CharacterInfoViewData? selected)
    {
        if (_isDrivingSelection) return;

        // 没有选中项:列表空了,或者正被选中的那一项刚从集合里摘掉(ListBox 会回写 null)
        if (selected == null)
        {
            Editor = null;
            OnPropertyChanged(nameof(IsEditingNew));
            return;
        }

        // 占位项与「重开同一个角色」都不该重建草稿:前者的草稿刚建好,后者会把改到一半的内容抹掉
        if (Editor != null && ReferenceEquals(Editor.Subject, selected.Data)) return;

        CharacterInfoViewData? previous = PreviousSelectionOf(selected);
        if (Editor is { IsDirty: true })
        {
            EConfirmChoice choice = await _messageService.ConfirmWithCancelAsync(Lang.CharacterEditorDirtyTips);
            if (choice == EConfirmChoice.Cancel)
            {
                RevertSelection(previous);
                return;
            }

            // 保存失败(名字空着)也算不能走:让用户回去把它填上
            if (choice == EConfirmChoice.Yes && !Editor.TryCommit())
            {
                RevertSelection(previous);
                return;
            }
        }

        // 干净的新角色切走时同样要撤掉占位项——没弹过窗不等于它该留下
        LeaveNewCharacter();
        Editor = CreateEditorFor(selected);
        OnPropertyChanged(nameof(IsEditingNew));
    }

    /// <summary>
    /// 走开之前问一句。给「新建」这类不经选中项的入口用。
    /// </summary>
    /// <returns>可以走开返回 True；用户选了「继续编辑」返回 False</returns>
    private async Task<bool> ConfirmLeaveEditorAsync()
    {
        if (Editor is { IsDirty: true })
        {
            EConfirmChoice choice = await _messageService.ConfirmWithCancelAsync(Lang.CharacterEditorDirtyTips);
            if (choice == EConfirmChoice.Cancel) return false;
            if (choice == EConfirmChoice.Yes && !Editor.TryCommit()) return false;
        }

        LeaveNewCharacter();
        return true;
    }

    /// <summary>
    /// 在这段里改选中项是<b>我们自己在驱动</b>，不是用户在切角色：不问去留、不重建草稿。
    /// 撤占位项这类动作会让 ListBox 先回写一次 null 再落到别的项上，那几次跳动都得挡掉。
    /// </summary>
    /// <param name="action">要执行的那段选中项变更</param>
    private void DrivingSelection(Action action)
    {
        _isDrivingSelection = true;
        try
        {
            action();
        }
        finally
        {
            _isDrivingSelection = false;
        }
    }

    /// <summary>选中项要回滚到哪一项：草稿正编辑着的那个角色</summary>
    private CharacterInfoViewData? PreviousSelectionOf(CharacterInfoViewData selected)
    {
        if (Editor == null) return null;
        foreach (CharacterInfoViewData item in CharacterListViewData.Characters)
        {
            if (!ReferenceEquals(item, selected) && ReferenceEquals(item.Data, Editor.Subject)) return item;
        }

        return null;
    }

    private void RevertSelection(CharacterInfoViewData? previous)
    {
        if (previous == null) return;
        DrivingSelection(() => CharacterListViewData.SelectedCharacter = previous);
    }

    /// <summary>给选中项开一份草稿；没有选中项时右主区显示空态</summary>
    private static CharacterDraft? CreateEditorFor(CharacterInfoViewData? selected) =>
        selected == null ? null : CharacterDraft.ForEdit(selected.Data);
}
