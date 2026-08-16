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

using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Shared.Data;

public class MenuItemViewData : ObservableObject
{
    private string? _menuHeader;

    public string? MenuHeader
    {
        get => _menuHeader;
        set
        {
            if (SetProperty(ref _menuHeader, value)) OnPropertyChanged(nameof(MenuTooltip));
        }
    }

    public string? MenuHeaderResourceKey { get; set; }
    public string? MenuIconName { get; set; }
    public MenuPages Key { get; set; }
    public string? Status { get; set; }

    public bool IsSeparator { get; set; }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value)) NotifyBadgeChanged();
        }
    }

    private bool _isBusy;

    /// <summary>本页有会话在跑</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value)) NotifyBadgeChanged();
        }
    }

    private bool _isAwaitingApproval;

    /// <summary>本页有会话卡在工具审批上等人回应</summary>
    public bool IsAwaitingApproval
    {
        get => _isAwaitingApproval;
        set
        {
            if (SetProperty(ref _isAwaitingApproval, value)) NotifyBadgeChanged();
        }
    }

    /// <summary>
    /// 显示「在跑」角标。只在未选中时显示：选中意味着这一页就在眼前，
    /// 页内的会话列表与对话区已经把状态说清楚了
    /// </summary>
    public bool ShowRunningBadge => !IsSelected && IsBusy && !IsAwaitingApproval;

    /// <summary>显示「待审批」角标（未选中时才显示，优先于在跑）</summary>
    public bool ShowApprovalBadge => !IsSelected && IsAwaitingApproval;

    /// <summary>悬停提示：菜单名，忙的时候追加一行状态（角标只有颜色，说不清是哪种忙）</summary>
    public string MenuTooltip
    {
        get
        {
            if (!IsBusy && !IsAwaitingApproval) return MenuHeader ?? string.Empty;
            string status = LocalizationManager.Instance.GetString(
                IsAwaitingApproval ? "SessionStatusAwaitingApproval" : "AgentStatusRunning");
            return $"{MenuHeader}\n{status}";
        }
    }

    private void NotifyBadgeChanged()
    {
        OnPropertyChanged(nameof(ShowRunningBadge));
        OnPropertyChanged(nameof(ShowApprovalBadge));
        OnPropertyChanged(nameof(MenuTooltip));
    }

    public ObservableCollection<MenuItemViewData> Children { get; set; } = new();

    public ICommand ActivateCommand { get; set; }

    public MenuItemViewData()
    {
        ActivateCommand = new RelayCommand(OnActivate);
    }

    private void OnActivate()
    {
        if (IsSeparator) return;
        // Messenger.Send(Key);
        // WeakReferenceMessenger.Default.Send(Key);
        App.JumpToPage(Key);
    }
}
