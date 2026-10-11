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
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 一个会话的标签页：页面、最近一份快照（树、编号、下拉框选项归属）、等着随下一次结果交给模型的旁注。
/// 同一标签页上的调用一个个来。这里管打开与拍快照，动作在 <c>PuppeteerTab.Actions.cs</c>，
/// 读正文、截图与回查在 <c>PuppeteerTab.Views.cs</c>。
/// </summary>
internal sealed partial class PuppeteerTab
{
    private const int NavigationTimeoutMs = 30_000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentQueue<string> _notes = new();
    private readonly IPage _page;
    private DateTime _lastUsed = DateTime.UtcNow;
    private volatile bool _handedOver; //用户点了「切到浏览器」在里面操作：不回收、弹窗留给他，智能体再动手时结束
    private Dialog? _strandedDialog; //接管中弹出、用户没理的弹窗：智能体再动手前关掉，不然页面一直卡着
    private IPage? _openedPage; //页面新开的标签页（target=_blank、window.open）：它抢走前台，本页就不出渲染帧、动作全超时
    private AxNode? _tree;
    private IReadOnlyDictionary<string, AxNode> _refs = new Dictionary<string, AxNode>();
    private readonly AxRoundTracker _round = new(); //这一轮开始时的编号表：模型写这条回复时看到的
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
        page.Popup += (_, e) => _openedPage = e.PopupPage;
        page.Close += (_, _) => Closed?.Invoke();
    }

    /// <summary>页面被关掉（用户关了这个标签页）</summary>
    public event Action? Closed;

    /// <summary>页面是否已关</summary>
    public bool IsClosed => _page.IsClosed;

    /// <summary>最后一次被调用（或被用户接手）的时刻（闲置回收看它）</summary>
    public DateTime LastUsed => _lastUsed;

    /// <summary>是否正交给用户操作（接管中）：闲置回收跳过它，页面弹窗不替用户点</summary>
    public bool IsHandedOver => _handedOver;

    /// <summary>
    /// 打开地址
    /// </summary>
    /// <param name="url">地址</param>
    /// <param name="cancellationToken">取消令牌（只管排队）</param>
    /// <returns>打开后的页面</returns>
    public Task<BrowserPageView> NavigateAsync(string url, CancellationToken cancellationToken) => RunAsync(async () =>
    {
        string? note = null;
        DateTime started = DateTime.UtcNow;
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
        catch (NavigationException e) when (e.Message.Contains("ERR_ABORTED", StringComparison.Ordinal))
        {
            // 地址本身是个文件：Chrome 把它当下载、导航中止，页面留在原处
            if (!await DownloadStartsAsync(started)) throw;
            note = "That address is a file download, so the page did not change.";
        }

        await SettleAsync();
        await WaitDownloadsAsync(started);
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
            DateTime started = DateTime.UtcNow;
            string note = await PerformAndWaitAsync(request);
            await ReclaimFrontAsync();
            await SettleAsync();
            await WaitDownloadsAsync(started);
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
        _handedOver = false; //智能体又动手了：接管到此为止
        _round.Enter(_refs);
        try
        {
            if (Interlocked.Exchange(ref _strandedDialog, null) is { } stranded) await DismissStrandedAsync(stranded);
            await ReclaimFrontAsync(); //页面脚本过后才开的新标签页
            return await work();
        }
        finally
        {
            _lastUsed = DateTime.UtcNow;
            _gate.Release();
        }
    }

    // 新标签页留着（可能是用户要看的内容、登录弹窗），本页拿回前台继续可操作，并告诉模型去处
    private async Task ReclaimFrontAsync()
    {
        if (_handedOver || Interlocked.Exchange(ref _openedPage, null) is not { } opened) return;
        try
        {
            await _page.BringToFrontAsync();
        }
        catch (PuppeteerException e)
        {
            Log.Debug($"[Browser] bringing the tab back to front failed: {e.Message}");
        }

        _notes.Enqueue($"That opened a new tab ({opened.Url}). It was left open; this tab is still the one you control. " +
                       "Navigate here to that address if you need its content.");
    }

    private async Task<BrowserPageView> CaptureAsync(string? actionNote)
    {
        AxNode tree = await ReadTreeAsync();
        AxSnapshot snapshot = AxSnapshotWriter.Write(tree);
        _tree = tree;
        _refs = snapshot.Refs;
        _pickerOptions = snapshot.PickerOptions;

        List<string> notes = DrainNotes(actionNote);
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
                    return root != null
                        ? ToAxNode(root, await FindPasswordFieldsAsync(root))
                        : new AxNode("RootWebArea", string.Empty);
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

    // 密码框在无障碍树上只是 textbox：逐个问页面。并发着问（CDP 本就流水线），输入框多的后台页也不拖慢每一步
    private static async Task<HashSet<SerializedAXNode>> FindPasswordFieldsAsync(SerializedAXNode root)
    {
        List<SerializedAXNode> boxes = new();
        Stack<SerializedAXNode> pending = new([root]);
        while (pending.TryPop(out SerializedAXNode? node))
        {
            foreach (SerializedAXNode child in node.Children ?? []) pending.Push(child);
            if (node.Role == "textbox") boxes.Add(node);
        }

        bool[] secret = await Task.WhenAll(boxes.Select(IsPasswordNodeAsync));
        HashSet<SerializedAXNode> found = new(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < boxes.Count; i++)
        {
            if (secret[i]) found.Add(boxes[i]);
        }

        return found;
    }

    private static async Task<bool> IsPasswordNodeAsync(SerializedAXNode node)
    {
        try
        {
            if (await node.ElementHandleAsync() is not { } handle) return false;
            try
            {
                return await handle.EvaluateFunctionAsync<bool>(IsPasswordScript);
            }
            finally
            {
                await handle.DisposeAsync();
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false; //元素刚好没了、执行上下文换了：当不是，别让一个输入框拖垮整份快照
        }
    }

    // 用户已经自己点掉了的再关会抛，吞掉即可
    private async Task DismissStrandedAsync(Dialog dialog)
    {
        try
        {
            await dialog.Dismiss();
            _notes.Enqueue($"A {dialog.DialogType} dialog \"{dialog.Message}\" was left open after the user handed " +
                           "the browser back; it was dismissed.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Debug($"[Browser] stranded dialog already closed: {e.Message}");
        }
    }

    private static AxNode ToAxNode(SerializedAXNode node, HashSet<SerializedAXNode> secrets) =>
        new(node.Role ?? string.Empty, node.Name ?? string.Empty)
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
        Description = node.Description ?? string.Empty,
        Url = node.Url ?? string.Empty,
        Secret = secrets.Contains(node),
        Children = node.Children?.Select(child => ToAxNode(child, secrets)).ToArray() ?? [],
        Source = node,
    };

    private static bool IsExpandable(SerializedAXNode node) =>
        string.Equals(node.Role, "combobox", StringComparison.OrdinalIgnoreCase) ||
        node.HasPopup is { Length: > 0 } popup && popup != "false";

    // 离开页面的确认要放行，否则导航卡住；其余弹窗一律关掉，内容记下随下一次结果交给模型。
    // 接管中不碰：「确认支付？」得由用户自己点，浏览器原样弹给他
    private async void OnDialog(Dialog dialog)
    {
        if (_handedOver)
        {
            _strandedDialog = dialog;
            return;
        }

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
