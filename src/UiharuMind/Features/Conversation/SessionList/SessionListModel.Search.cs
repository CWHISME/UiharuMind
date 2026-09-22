using System;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Features.Conversation.SessionList;

/// <summary>
/// 会话列表的搜索过滤：标题与描述双字段、大小写不敏感（与 <c>CharacterPickerViewData</c> 同口径）。
///
/// 搜到空不丢选中：被滤掉的只是"当前不可见"，中间还在看它；
/// 选中保留在全量里，清掉搜索词就原样回来（见 <c>RestoreSelection</c> 按全量找回）。
/// </summary>
public partial class SessionListModel
{
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>当前有搜索词（界面据此可提示"无匹配"之类）</summary>
    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>列表 chrome（搜索行显隐）刷新：条目数、搜索词、批量模式任一变化都调</summary>
    private void RefreshListChrome()
    {
        OnPropertyChanged(nameof(HasVisibleSessions));
        OnPropertyChanged(nameof(ShowSearchRow));
    }

    /// <summary>当前有可见条目（搜索行没东西可筛时可以收起来）</summary>
    public bool HasVisibleSessions => Sessions.Count > 0;

    /// <summary>搜索行是否显示：有条目可筛、或正在搜、或在批量模式（退出要从这里点）</summary>
    public bool ShowSearchRow => HasVisibleSessions || HasSearch || IsBatchMode;

    partial void OnSearchTextChanged(string value)
    {
        Sync();
        OnPropertyChanged(nameof(HasSearch));
        RefreshListChrome();
    }

    private bool MatchesSearch(ChatSessionMeta meta)
    {
        string keyword = SearchText.Trim();
        if (keyword.Length == 0) return true;
        return meta.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
               || meta.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
