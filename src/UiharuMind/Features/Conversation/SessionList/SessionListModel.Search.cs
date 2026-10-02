using System;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Features.Conversation.SessionList;

/// <summary>
/// 会话列表的搜索过滤：标题与描述双字段、大小写不敏感（与 <c>CharacterPickerViewData</c> 同口径）；
/// 同一个词顺带在消息里搜一遍（<see cref="ContentSearch"/>）。
///
/// 搜到空不丢选中：被滤掉的只是"当前不可见"，中间还在看它；
/// 选中保留在全量里，清掉搜索词就原样回来（见 <c>RestoreSelection</c> 按全量找回）。
/// </summary>
public partial class SessionListModel
{
    [ObservableProperty] private string _searchText = string.Empty;

    private string? _pinnedSessionId; //标题过滤的唯一例外:从「消息里提到的」点开的那个,不匹配也留在列表里

    /// <summary>同一个搜索词在这一侧全部会话的消息里搜到的（列表下方「消息里提到的」那一段）</summary>
    public SessionContentSearchViewData ContentSearch { get; }

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

    /// <summary>
    /// 选中从「消息里提到的」点开的会话。标题未必匹配搜索词，不在显示集合里的话列表框认不出选中、
    /// 会把它写回空——所以先把它钉在列表里（换搜索词就不钉了；再点开别的就换成钉那个），再走用户点选同一路。
    /// 扫完之后被删了的就什么都不做
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public void SelectFromContentSearch(string sessionId)
    {
        if (IndexOfAll(sessionId) < 0) return;

        // 换钉的那一下,上一个钉住的(正选着)被摘出显示集合,列表框把选中写回空——那不是用户选的,不该切到空态
        SessionListItem? selected = SelectedSession;
        _pinnedSessionId = sessionId;
        _suppressSelectionNotify = true;
        try
        {
            RebuildFiltered();
        }
        finally
        {
            RestoreSelection(selected);
            _suppressSelectionNotify = false;
        }

        if (Find(sessionId) is { } item) SelectedSession = item;
    }

    partial void OnSearchTextChanged(string value)
    {
        _pinnedSessionId = null;
        Sync();
        ContentSearch.SetQuery(value);
        OnPropertyChanged(nameof(HasSearch));
        RefreshListChrome();
    }

    private bool MatchesSearch(ChatSessionMeta meta)
    {
        string keyword = SearchText.Trim();
        if (keyword.Length == 0 || meta.SessionId == _pinnedSessionId) return true;
        return meta.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
               || meta.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
