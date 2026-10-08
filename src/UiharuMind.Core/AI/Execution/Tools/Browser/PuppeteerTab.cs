/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using PuppeteerSharp;
using PuppeteerSharp.PageAccessibility;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 一个会话的标签页：页面、最近一份快照（树、编号、下拉框选项归属）、等着随下一次结果交给模型的旁注。
/// 同一标签页上的调用一个个来。这里管打开与拍快照，动作在 <c>PuppeteerTab.Actions.cs</c>。
/// </summary>
internal sealed partial class PuppeteerTab
{
    private const int NavigationTimeoutMs = 30_000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentQueue<string> _notes = new();
    private readonly IPage _page;
    private DateTime _lastUsed = DateTime.UtcNow;
    private AxNode? _tree;
    private IReadOnlyDictionary<string, AxNode> _refs = new Dictionary<string, AxNode>();
    private IReadOnlyDictionary<AxNode, AxNode> _pickerOptions =
        new Dictionary<AxNode, AxNode>(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// 接管一个新开的页面
    /// </summary>
    /// <param name="page">页面</param>
    public PuppeteerTab(IPage page)
    {
        _page = page;
        page.Dialog += (_, e) => OnDialog(e.Dialog);
        page.Close += (_, _) => Closed?.Invoke();
    }

    /// <summary>页面被关掉（用户关了这个标签页）</summary>
    public event Action? Closed;

    /// <summary>页面是否已关</summary>
    public bool IsClosed => _page.IsClosed;

    /// <summary>最后一次被调用的时刻（闲置回收看它）</summary>
    public DateTime LastUsed => _lastUsed;

    /// <summary>
    /// 打开地址
    /// </summary>
    /// <param name="url">地址</param>
    /// <param name="cancellationToken">取消令牌（只管排队）</param>
    /// <returns>打开后的页面</returns>
    public Task<BrowserPageView> NavigateAsync(string url, CancellationToken cancellationToken) => RunAsync(async () =>
    {
        string? note = null;
        try
        {
            // 只等文档就绪，不等 load：慢图、慢脚本、第三方统计会把 load 拖过超时，正文早就能看了
            await _page.GoToAsync(url, new NavigationOptions
            {
                Timeout = NavigationTimeoutMs,
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
            });
        }
        catch (NavigationException e) when (e.InnerException is TimeoutException ||
                                            e.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            // 冷启动的慢站点：交回此刻已有的内容让模型决定等还是换路，报错只会让它从头再开一次
            note = $"The page was still loading after {NavigationTimeoutMs / 1000} s; this is what it shows so far. " +
                   "Take a snapshot again in a moment.";
        }

        await SettleAsync();
        return await CaptureAsync(note);
    }, cancellationToken);

    /// <summary>
    /// 重拍快照
    /// </summary>
    /// <param name="cancellationToken">取消令牌（只管排队）</param>
    /// <returns>当前页面</returns>
    public Task<BrowserPageView> SnapshotAsync(CancellationToken cancellationToken) =>
        RunAsync(() => CaptureAsync(null), cancellationToken);

    /// <summary>
    /// 做一个动作，做完（等可能的跳转落定）重拍快照
    /// </summary>
    /// <param name="request">动作</param>
    /// <param name="cancellationToken">取消令牌（只管排队）</param>
    /// <returns>动作之后的页面</returns>
    public Task<BrowserPageView> ActAsync(BrowserActionRequest request, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            string note = await PerformAndWaitAsync(request);
            await SettleAsync();
            return await CaptureAsync(note);
        }, cancellationToken);

    /// <summary>
    /// 关掉页面（闲置回收用）；正在跑的调用跑完再关
    /// </summary>
    /// <returns>关完为止</returns>
    public async Task CloseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_page.IsClosed) await _page.CloseAsync();
        }
        catch (PuppeteerException e)
        {
            Log.Debug($"[Browser] closing an idle tab failed: {e.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        _lastUsed = DateTime.UtcNow;
        try
        {
            return await work();
        }
        finally
        {
            _lastUsed = DateTime.UtcNow;
            _gate.Release();
        }
    }

    private async Task<BrowserPageView> CaptureAsync(string? actionNote)
    {
        AxNode tree = await ReadTreeAsync();
        AxSnapshot snapshot = AxSnapshotWriter.Write(tree);
        _tree = tree;
        _refs = snapshot.Refs;
        _pickerOptions = snapshot.PickerOptions;

        List<string> notes = new();
        if (actionNote != null) notes.Add(actionNote);
        while (_notes.TryDequeue(out string? note)) notes.Add(note);
        string title = await _page.GetTitleAsync();
        return new BrowserPageView(_page.Url, title, snapshot.Text, snapshot.CompactText, notes);
    }

    // 拍的那一刻页面可能还在换（迟到的跳转、刚提交）：拍到异常或空页就等它落定再拍，最多三次
    private async Task<AxNode> ReadTreeAsync()
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                SerializedAXNode? root = await _page.Accessibility.SnapshotAsync(new AccessibilitySnapshotOptions
                {
                    InterestingOnly = true,
                    IncludeIframes = true,
                });
                if (root is { Children.Length: > 0 } || attempt == 3)
                {
                    return root != null ? ToAxNode(root) : new AxNode("RootWebArea", string.Empty);
                }
            }
            catch (PuppeteerException) when (attempt < 3)
            {
            }

            await Task.Delay(500);
            await SettleAsync();
        }
    }

    // SPA 渲染、点击后的跳转：等网络静下来一小会儿；长连接、轮询的页面永远静不下来，等不到也照常往下走
    private async Task SettleAsync()
    {
        try
        {
            await _page.WaitForNetworkIdleAsync(new WaitForNetworkIdleOptions { IdleTime = 500, Timeout = 3000 });
        }
        catch (Exception e) when (e is TimeoutException or PuppeteerException)
        {
            Log.Debug($"[Browser] network not idle: {e.Message}");
        }
    }

    private static AxNode ToAxNode(SerializedAXNode node) => new(node.Role ?? string.Empty, node.Name ?? string.Empty)
    {
        Value = node.Value ?? string.Empty,
        Checked = node.Checked switch
        {
            CheckedState.True => "true",
            CheckedState.Mixed => "mixed",
            _ => "false",
        },
        Selected = node.Selected,
        Disabled = node.Disabled,
        Pressed = node.Pressed == CheckedState.True,
        // PuppeteerSharp 的 Expanded 是普通 bool，「收起」与「无所谓展开」都是 false：
        // 只有下拉框与带弹出菜单的元素才把 false 当收起
        Expanded = node.Expanded ? true : IsExpandable(node) ? false : null,
        Level = node.Level,
        Children = node.Children?.Select(ToAxNode).ToArray() ?? [],
        Source = node,
    };

    private static bool IsExpandable(SerializedAXNode node) =>
        string.Equals(node.Role, "combobox", StringComparison.OrdinalIgnoreCase) ||
        node.HasPopup is { Length: > 0 } popup && popup != "false";

    // 离开页面的确认要放行，否则导航卡住；其余弹窗一律关掉，内容记下随下一次结果交给模型
    private async void OnDialog(Dialog dialog)
    {
        try
        {
            if (dialog.DialogType == DialogType.BeforeUnload)
            {
                await dialog.Accept();
                return;
            }

            _notes.Enqueue($"The page showed a {dialog.DialogType} dialog \"{dialog.Message}\"; it was dismissed.");
            await dialog.Dismiss();
        }
        catch (Exception e)
        {
            Log.Warning($"[Browser] dialog handling failed: {e.Message}");
        }
    }
}
