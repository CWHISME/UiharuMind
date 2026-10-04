using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UiharuMind.Shared.Controls;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 设置窗口的全局搜索：把各页的 <see cref="SettingsRow"/> 收成一份索引，
/// 输入即过滤，点一条跳到那一页并把它滚进视野。
///
/// 索引按「页面 XAML 树」遍历，而不是「已渲染的可视树」：TabControl 未选中的 Tab
/// 内容还没被模板呈现，可视树里翻不到，逻辑树上也未必挂着，
/// 所以遍历时对内容型控件（ContentControl / Decorator / Panel，以及共用件的
/// Body / Field 两个插槽）逐个下钻，页面声明的行一条都不漏。
/// </summary>
public partial class SettingsWindow
{
    private readonly List<SettingsSearchHit> _searchHits = [];
    private bool _searchIndexBuilt;

    /// <summary>输入即过滤：空串收起结果面板，非空则列出命中的行</summary>
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        string keyword = SearchBox.Text?.Trim() ?? string.Empty;
        if (keyword.Length == 0)
        {
            SearchResultPanel.IsVisible = false;
            SearchResultList.ItemsSource = null;
            return;
        }

        EnsureSearchIndex();

        // 页名与行标题任一命中即可：搜「快捷键」能整页列出来，搜「主题」能定位到那一行
        List<SettingsSearchHit> hits = _searchHits
            .Where(hit => hit.Matches(keyword))
            .ToList();

        SearchResultList.ItemsSource = hits;
        SearchEmptyText.IsVisible = hits.Count == 0;
        SearchResultPanel.IsVisible = true;
    }

    /// <summary>点一条结果：切到那一页，并把那一行滚进视野</summary>
    private void OnSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (SearchResultList.SelectedItem is not SettingsSearchHit hit) return;

        SearchResultList.SelectedItem = null;
        SelectByTitleKey(hit.PageTitleKey);
        // 切页后目标行才挂进可视树，等这一轮布局走完再滚
        Dispatcher.UIThread.Post(() => hit.Row.BringIntoView());
        // 跳完收掉搜索：清空输入会顺带隐藏结果面板
        SearchBox.Text = string.Empty;
    }

    private void EnsureSearchIndex()
    {
        if (_searchIndexBuilt) return;
        _searchIndexBuilt = true;

        foreach (SettingsPageEntry page in _pages)
        {
            foreach (SettingsRow row in FindRows(page.Page))
            {
                _searchHits.Add(new SettingsSearchHit(page.TitleKey, row));
            }
        }
    }

    /// <summary>深度优先收集一棵页面树里的设置行</summary>
    private static IEnumerable<SettingsRow> FindRows(Control root)
    {
        if (root is SettingsRow row)
        {
            yield return row;
            yield break;
        }

        foreach (Control child in ChildControls(root))
        {
            foreach (SettingsRow found in FindRows(child))
            {
                yield return found;
            }
        }
    }

    /// <summary>
    /// 取一个控件的「下一层」。只走「内容位」一走到底：先用显式插槽（共用件的 Body / Field），
    /// 再退到框架自带的内容位（Panel / Decorator / ItemsControl / Content / 逻辑子级）。
    /// <b>只取第一个命中的分支</b>：不然 Panel 那类控件的 Children 会从回退分支再收一遍，
    /// 同一行在搜索结果里出现两次。
    /// </summary>
    private static IEnumerable<Control> ChildControls(Control control)
    {
        switch (control)
        {
            case SettingsSection section when section.Body is Control body:
                return [body];
            case SettingsRow settingsRow when settingsRow.Field is Control field:
                return [field];
            case Panel panel:
                return panel.Children;
            case Decorator { Child: Control decoratorChild }:
                return [decoratorChild];
            // TabControl 未选中的 Tab：内容还没被模板呈现，只能从 Items 里拿
            case ItemsControl items:
                return items.Items.OfType<Control>();
            case ContentControl { Content: Control content }:
                return [content];
            default:
                return control.GetLogicalChildren().OfType<Control>();
        }
    }
}

/// <summary>一条搜索命中：页名 key + 命中的那一行</summary>
/// <param name="PageTitleKey">页标题 key</param>
/// <param name="Row">命中的设置行</param>
internal sealed record SettingsSearchHit(string PageTitleKey, SettingsRow Row)
{
    /// <summary>结果行文案：「页名 · 行标题」</summary>
    public string Display
    {
        get
        {
            string page = Loc.Text(PageTitleKey);
            string header = Row.Header ?? string.Empty;
            return header.Length == 0 ? page : $"{page} · {header}";
        }
    }

    /// <summary>页名或行标题命中关键字即算命中（忽略大小写）</summary>
    /// <param name="keyword">搜索词</param>
    public bool Matches(string keyword) =>
        Display.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
