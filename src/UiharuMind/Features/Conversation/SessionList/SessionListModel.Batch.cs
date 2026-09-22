using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Generated;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;namespace UiharuMind.Features.Conversation.SessionList;

/// <summary>
/// 会话列表的批量模式：勾选与展示选中是正交的两套状态（见 <see cref="SessionListItem.IsBatchChecked"/>）。
///
/// 删一批只弹一次确认框（与 <c>ConversationItemActions.DeleteTurn</c> 同口径）；
/// 勾选含隐藏条目也算数（搜出来勾上再换词，被滤掉的不丢勾）——删的口径是"勾了的"，
/// 不是"看得见的"。退出模式统一清勾，不留脏状态给下次。
/// </summary>
public partial class SessionListModel
{
    [ObservableProperty] private bool _isBatchMode;

    /// <summary>当前勾了几个（含被搜索滤掉的）</summary>
    public int CheckedCount => _all.Count(x => x.IsBatchChecked);

    /// <summary>有没有勾选（删除按钮据此可用）</summary>
    public bool HasChecked => CheckedCount > 0;

    /// <summary>当前可见的是不是全勾了（全选键再点即清空的判据）</summary>
    public bool AreAllVisibleChecked => Sessions.Count > 0 && Sessions.All(x => x.IsBatchChecked);

    partial void OnIsBatchModeChanged(bool value)
    {
        if (!value) ClearChecks();
        RefreshListChrome();
    }

    /// <summary>进出批量模式</summary>
    [RelayCommand]
    private void ToggleBatchMode() => IsBatchMode = !IsBatchMode;

    /// <summary>全选/清空当前可见的：没全勾就全勾上，全勾了就一次清空（再点即反悔）</summary>
    [RelayCommand]
    private void ToggleSelectAll()
    {
        bool select = !AreAllVisibleChecked;
        foreach (SessionListItem item in Sessions) item.IsBatchChecked = select;
        RefreshCheckedState();
    }

    /// <summary>
    /// 删除勾选的会话：一次确认，正在跑的跳过并事后告知条数。
    /// 单条删除走 <see cref="SessionListItem.DeleteCommand"/>（逐条确认），这里不复用它
    /// </summary>
    [RelayCommand]
    private async Task DeleteCheckedAsync()
    {
        // 先快照成实例：等确认框期间落盘可能增删条目，按实例删才不会误伤
        List<SessionListItem> targets = _all.Where(x => x.IsBatchChecked).ToList();
        if (targets.Count == 0) return;
        if (!await _messageService.ConfirmAsync(
                string.Format(Loc.Text(LangKey.SessionBatchDeleteConfirmFormat), targets.Count))) return;

        int skipped = 0;
        foreach (SessionListItem item in targets)
        {
            if (!item.CanMutateFiles)
            {
                skipped++;
                continue;
            }

            SessionManager.Instance.Delete(item.SessionId);
            Remove(item.SessionId);
        }

        // 操作完收工：删掉的随条目消失，跳过的也清勾（想重试再勾，不留一个永远亮着的删除键）
        foreach (SessionListItem item in targets) item.IsBatchChecked = false;
        RefreshCheckedState();

        if (skipped > 0)
            await _messageService.ShowInfoAsync(
                string.Format(Loc.Text(LangKey.SessionBatchSkippedFormat), skipped));
    }

    private void ClearChecks()
    {
        foreach (SessionListItem item in _all)
        {
            if (item.IsBatchChecked) item.IsBatchChecked = false;
        }

        RefreshCheckedState();
    }

    private void RefreshCheckedState()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(AreAllVisibleChecked));
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionListItem.IsBatchChecked)) RefreshCheckedState();
    }
}
