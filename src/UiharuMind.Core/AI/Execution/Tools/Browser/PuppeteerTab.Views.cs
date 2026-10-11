/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using PuppeteerSharp;
using PuppeteerSharp.Media;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.WebTools;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 看页面的另几种方式：读正文、截图；另有不碰页面的回查（审批卡要说清点的是什么）与把窗口切到最前（接管）
/// </summary>
internal sealed partial class PuppeteerTab
{
    private const int ScreenshotQuality = 80;

    /// <summary>当前地址</summary>
    public string Url => _page.Url;

    /// <summary>
    /// 按最近一份快照把目标（编号或看得见的文字）翻回元素，不碰页面
    /// </summary>
    /// <param name="target">模型给的目标</param>
    /// <returns>元素；翻不出为 null</returns>
    public AxNode? Peek(string target)
    {
        string key = AxSnapshotSearch.NormalizeTarget(target);
        return key.Length == 0 ? null : Lookup(key);
    }

    /// <summary>
    /// 读当前页面的正文（Markdown），与 WebFetch 同一套抽取与站点清理；不编号
    /// </summary>
    /// <param name="cancellationToken">取消令牌（只管排队）</param>
    /// <returns>正文</returns>
    public Task<BrowserTextView> ReadTextAsync(CancellationToken cancellationToken) => RunAsync(async () =>
    {
        string html = await _page.GetContentAsync();
        string text = await DirectPageReader.ExtractTextAsync(html, cancellationToken);
        return new BrowserTextView(_page.Url, await _page.GetTitleAsync(), SitePageCleaners.Clean(_page.Url, text),
            DrainNotes(null));
    }, cancellationToken);

    /// <summary>
    /// 截当前页面看得见的那一屏
    /// </summary>
    /// <param name="cancellationToken">取消令牌（只管排队）</param>
    /// <returns>截图（JPEG）</returns>
    public Task<BrowserScreenshot> ScreenshotAsync(CancellationToken cancellationToken) => RunAsync(async () =>
    {
        // JPEG：视网膜屏上是两倍像素，PNG 动辄几 MB；看图的那一侧本来也要再缩
        byte[] image = await _page.ScreenshotDataAsync(new ScreenshotOptions
        {
            Type = ScreenshotType.Jpeg,
            Quality = ScreenshotQuality,
        });
        return new BrowserScreenshot(_page.Url, await _page.GetTitleAsync(), image, DrainNotes(null));
    }, cancellationToken);

    /// <summary>
    /// 交给用户：把窗口切到最前（macOS 上连 Chrome 一起切到前台），并记成接管中——
    /// 闲置回收跳过它，页面弹窗留给用户点，直到智能体再动这个标签页。
    /// 不排队：用户点的时候工具可能正等着页面
    /// </summary>
    /// <returns>切成为 true；页面已关为 false</returns>
    public async Task<bool> ShowAsync()
    {
        try
        {
            if (_page.IsClosed) return false;
            _handedOver = true;
            _lastUsed = DateTime.UtcNow;
            await _page.BringToFrontAsync();
            return true;
        }
        catch (PuppeteerException e)
        {
            Log.Debug($"[Browser] bring to front failed: {e.Message}");
            return false;
        }
    }

    // 编号直接查表；像编号却查不到的不再按文字找（那是过期编号）；其余按看得见的文字找（div 做的按钮）
    private AxNode? Lookup(string key)
    {
        if (_refs.TryGetValue(key, out AxNode? byRef)) return byRef;
        if (RefPattern().IsMatch(key) || _tree == null) return null;
        return AxSnapshotSearch.FindByText(_tree, key);
    }

    // 这一步的说明在前，攒着的旁注（弹窗被关掉了）在后
    private List<string> DrainNotes(string? first)
    {
        List<string> notes = new();
        if (first != null) notes.Add(first);
        while (_notes.TryDequeue(out string? note)) notes.Add(note);
        return notes;
    }
}
