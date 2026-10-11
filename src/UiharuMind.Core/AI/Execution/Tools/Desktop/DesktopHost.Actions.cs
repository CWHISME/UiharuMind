/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Globalization;
using System.Text.RegularExpressions;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.AI.Execution.Tools.Desktop;

/// <summary>
/// 桌面宿主的动作：三级逐级退（ADR 0084）——无障碍动作 → 投递给进程的事件 → 真实键鼠（只在模型明说时）
/// </summary>
public sealed partial class DesktopHost
{
    private const int SettleStepMs = 300; //动作后隔这么久重读一次，两次一样就算稳了
    private const int SettleReads = 3;
    private const int ScrollLinesPerStep = 5;
    private const double UserIdleSeconds = 1.0; //用户刚动过键鼠就先等等，免得抢他的输入
    private const int UserIdleWaitMs = 3000;
    private const int TypingChunk = 24; //真实打字分段打，段间看用户有没有动

    private readonly SemaphoreSlim _realInput = new(1, 1); //真实键鼠全局只有一份

    /// <summary>动作后隔多久重读（测试调短）</summary>
    internal int SettleDelayMs { get; init; } = SettleStepMs;

    /// <summary>
    /// 在窗口里做一个动作，做完重读快照
    /// </summary>
    /// <param name="sessionId">会话 id</param>
    /// <param name="request">动作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>动作之后的窗口</returns>
    /// <exception cref="AxActionException">目标找不到、拒绝往密码框打字、控件不支持</exception>
    public async Task<DesktopView> ActAsync(string sessionId, DesktopActionRequest request,
        CancellationToken cancellationToken)
    {
        DesktopWindow window = await ResolveAsync(request.Window, refresh: false, cancellationToken);
        WindowState state = _states.GetOrAdd((sessionId, window.Id), _ => new WindowState());
        SemaphoreSlim gate = _appGates.GetOrAdd(window.Info.Pid, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            state.Round.Enter(state.Refs);
            Target target = Resolve(window, state, request);
            string? note = await PerformAsync(window, state, request, target, cancellationToken);
            return await SettleAsync(window, state, note, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 用户接手完交回：把窗口提到前台给他（审批卡上点「切过去」时），交回后重拍
    /// </summary>
    /// <param name="sessionId">会话 id</param>
    /// <param name="window">窗口</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>交回时的窗口</returns>
    public async Task<DesktopView> AfterHandoffAsync(string sessionId, string window, CancellationToken cancellationToken)
    {
        DesktopView view = await SnapshotAsync(sessionId, window, cancellationToken);
        return view with { Notes = ["The user took over the window and handed it back. Check whether it worked.", .. view.Notes] };
    }

    /// <summary>
    /// 把窗口提到前台（接管卡上「切过去」）
    /// </summary>
    /// <param name="window">窗口编号、应用名或标题里的字</param>
    /// <returns>做成为 true</returns>
    public bool Show(string window) =>
        _platform != null && Unavailable == null && FindWindow(window) is { Blocked: null } found && _platform.Raise(found.Info);

    /// <summary>
    /// 启动应用或用默认应用打开文件，稍等后列出它的窗口
    /// </summary>
    /// <param name="target">应用名或文件的绝对路径</param>
    /// <param name="isFile">是不是文件</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>打开后的窗口（按应用名或文件名挑）</returns>
    public async Task<IReadOnlyList<DesktopWindow>> OpenAsync(string target, bool isFile, CancellationToken cancellationToken)
    {
        IDesktopPlatform platform = Platform();
        await Task.Run(() => platform.Open(target, isFile), cancellationToken);
        string wanted = isFile ? Path.GetFileName(target) : target;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            await Task.Delay(500, cancellationToken);
            IReadOnlyList<DesktopWindow> windows = await ListWindowsAsync(cancellationToken);
            DesktopWindow[] matched = windows.Where(window =>
                window.Info.App.Contains(wanted, StringComparison.OrdinalIgnoreCase) ||
                window.Info.Title.Contains(wanted, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matched.Length > 0) return matched;
        }

        return [];
    }

    private Target Resolve(DesktopWindow window, WindowState state, DesktopActionRequest request)
    {
        string given = request.Target.Trim();
        if (given.Length == 0) return new Target(null, null);

        if (CoordinatesPattern().Match(given) is { Success: true } match)
        {
            double x = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            double y = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            AxRect bounds = window.Info.Bounds;
            if (x < 0 || y < 0 || x >= bounds.Width || y >= bounds.Height)
            {
                throw new AxActionException($"({given}) is outside {window.Id}, which is {bounds.Width:0}×{bounds.Height:0}.");
            }

            return new Target(null, (bounds.X + x, bounds.Y + y));
        }

        if (state.Tree == null)
        {
            throw new AxActionException($"Take a snapshot of {window.Id} first, then use its refs.");
        }

        string key = AxSnapshotSearch.NormalizeTarget(given);
        AxNode? node = AxRefReconciler.Find(state.Round.Refs, state.Refs, key)
                       ?? (IsRef(key) ? null : AxSnapshotSearch.FindByText(state.Tree, key));
        return node != null
            ? new Target(node, null)
            : throw new AxActionException(IsRef(key)
                ? $"There is no {key} in the latest snapshot of {window.Id}. Take a new snapshot and use its refs."
                : $"Nothing in {window.Id} shows \"{given}\". Take a new snapshot and use its refs.");
    }

    private async Task<string?> PerformAsync(DesktopWindow window, WindowState state, DesktopActionRequest request,
        Target target, CancellationToken cancellationToken)
    {
        AxNode? node = target.Node;
        switch (request.Action)
        {
            case EDesktopAction.Click:
            case EDesktopAction.DoubleClick:
            case EDesktopAction.RightClick:
                return await ClickAsync(window, target, request.Action, request.Real, cancellationToken);

            case EDesktopAction.Check:
            case EDesktopAction.Uncheck:
                AxNode box = Required(node, request);
                string wanted = request.Action == EDesktopAction.Check ? "true" : "false";
                if ((box.Checked.Length > 0 ? box.Checked : "false") == wanted) return $"{box.Label} was already {(wanted == "true" ? "checked" : "unchecked")}.";
                return await ClickAsync(window, target, EDesktopAction.Click, request.Real, cancellationToken);

            case EDesktopAction.Fill:
                RefuseSecret(node, state, request);
                if (!request.Real && node != null && await AccessibleAsync(node, EDesktopAction.Fill, request.Value)) return null;
                await FocusAsync(window, target, request.Real, cancellationToken);
                await InputAsync(window, new DesktopInput(EDesktopInputKind.SelectAll), request.Real, cancellationToken);
                await TypeAsync(window, request.Value, request.Real, cancellationToken);
                return null;

            case EDesktopAction.Type:
                RefuseSecret(node, state, request);
                // 先试在光标处插入（不经键盘，后台也行）：目标没给就落在快照里有焦点的那个
                AxNode? into = node ?? (target.Point == null && state.Tree != null ? FocusedNode(state.Tree) : null);
                if (!request.Real && into != null && await AccessibleAsync(into, EDesktopAction.Type, request.Value))
                {
                    return null;
                }

                if (target.Node != null || target.Point != null) await FocusAsync(window, target, request.Real, cancellationToken);
                await TypeAsync(window, request.Value, request.Real, cancellationToken);
                return null;

            case EDesktopAction.Press:
                string[] keys = BrowserKeys.Parse(request.Value);
                if (keys.Length == 0) throw new AxActionException("press needs value: a key name such as Enter, Tab or Cmd+S.");
                if (BrowserKeys.TypesText(keys)) RefuseSecret(node, state, request);
                if (target.Node != null || target.Point != null) await FocusAsync(window, target, request.Real, cancellationToken);
                await InputAsync(window, new DesktopInput(EDesktopInputKind.Key) { Keys = keys }, request.Real, cancellationToken);
                return null;

            case EDesktopAction.Select:
                AxNode list = Required(node, request);
                if (request.Value.Trim().Length == 0) throw new AxActionException("select needs value: the visible text of the option.");
                if (!request.Real && await AccessibleAsync(list, EDesktopAction.Select, request.Value)) return null;
                throw new AxActionException($"Could not pick \"{request.Value}\" in {list.Label} directly. " +
                                            "Click it to open the list, then click the option.");

            case EDesktopAction.Focus:
                await FocusAsync(window, target, request.Real, cancellationToken);
                return null;

            case EDesktopAction.Scroll:
                int lines = request.Value.Trim().ToLowerInvariant() switch
                {
                    "up" => -ScrollLinesPerStep,
                    "down" or "" => ScrollLinesPerStep,
                    _ => throw new AxActionException("scroll needs value up or down."),
                };
                (double x, double y) = PointOf(window, target, allowWindowCenter: true);
                await InputAsync(window, new DesktopInput(EDesktopInputKind.Scroll) { X = x, Y = y, ScrollLines = lines },
                    request.Real, cancellationToken);
                return null;

            default:
                throw new AxActionException($"Unknown action {request.Action}.");
        }
    }

    // 第 1 级先试；做不了（或要双击、右键）按位置发事件，第 2 级投递、第 3 级真点
    private async Task<string?> ClickAsync(DesktopWindow window, Target target, EDesktopAction kind, bool real,
        CancellationToken cancellationToken)
    {
        if (target.Node == null && target.Point == null)
        {
            throw new AxActionException("click needs a target: a ref from the latest snapshot, visible text, or x,y.");
        }

        if (!real && kind == EDesktopAction.Click && target.Node is { } node)
        {
            if (node.Bounds is { } bounds) Highlight?.Invoke(bounds);
            if (await AccessibleAsync(node, EDesktopAction.Click, string.Empty)) return null;
        }

        (double x, double y) = PointOf(window, target, allowWindowCenter: false);
        DesktopInput click = new(EDesktopInputKind.Click)
        {
            X = x, Y = y, Clicks = kind == EDesktopAction.DoubleClick ? 2 : 1, RightButton = kind == EDesktopAction.RightClick,
        };
        await InputAsync(window, click, real, cancellationToken);
        return real ? null : "Clicked by sending the click to the app in the background; if nothing changed, the app may " +
                             "ignore background clicks: try again with input \"real\" (the user is asked first).";
    }

    private async Task FocusAsync(DesktopWindow window, Target target, bool real, CancellationToken cancellationToken)
    {
        if (!real && target.Node is { } node && await AccessibleAsync(node, EDesktopAction.Focus, string.Empty))
        {
            return;
        }

        if (target.Node != null || target.Point != null)
        {
            await ClickAsync(window, target, EDesktopAction.Click, real, cancellationToken);
        }
    }

    private async Task TypeAsync(DesktopWindow window, string text, bool real, CancellationToken cancellationToken)
    {
        if (text.Length == 0) return;
        if (!real)
        {
            await InputAsync(window, new DesktopInput(EDesktopInputKind.Text) { Text = text }, real: false, cancellationToken);
            return;
        }

        for (int start = 0; start < text.Length; start += TypingChunk)
        {
            string chunk = text.Substring(start, Math.Min(TypingChunk, text.Length - start));
            await InputAsync(window, new DesktopInput(EDesktopInputKind.Text) { Text = chunk }, real: true, cancellationToken);
        }
    }

    // 真实键鼠：全局一份，先等用户停手，提到前台再发；用户一直在动就放弃，交给模型去问
    private async Task InputAsync(DesktopWindow window, DesktopInput input, bool real, CancellationToken cancellationToken)
    {
        IDesktopPlatform platform = _platform!;
        if (input.Kind is EDesktopInputKind.Click or EDesktopInputKind.Scroll)
        {
            Highlight?.Invoke(new AxRect(input.X - 12, input.Y - 12, 24, 24));
        }

        if (!real)
        {
            await OnPlatformAsync(() =>
            {
                platform.Input(window.Info, input, real: false);
                return true;
            });
            return;
        }

        await _realInput.WaitAsync(cancellationToken);
        try
        {
            await WaitForUserIdleAsync(cancellationToken);
            await OnPlatformAsync(() => platform.Raise(window.Info));
            await Task.Delay(200, cancellationToken);
            await OnPlatformAsync(() =>
            {
                platform.Input(window.Info, input, real: true);
                return true;
            });
        }
        finally
        {
            _realInput.Release();
        }
    }

    private async Task WaitForUserIdleAsync(CancellationToken cancellationToken)
    {
        for (int waited = 0; _platform!.SecondsSinceUserInput < UserIdleSeconds; waited += 250)
        {
            if (waited >= UserIdleWaitMs)
            {
                throw new AxActionException("The user is using the mouse or keyboard right now, so real input was not sent. " +
                                            "Try again in a moment, or ask the user to do this step.");
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    // 动作后等界面稳下来：连着两次读到一样的就交，最多读几次；稳了再看要不要认图里的字（只认一次，认字慢）
    private async Task<DesktopView> SettleAsync(DesktopWindow window, WindowState state, string? note,
        CancellationToken cancellationToken)
    {
        List<string> notes = note != null ? [note] : [];
        AxNode? tree = null;
        string previous = string.Empty;
        try
        {
            for (int read = 0; read < SettleReads; read++)
            {
                await Task.Delay(SettleDelayMs, cancellationToken);
                tree = await Task.Run(() => _platform!.ReadWindow(window.Info, MaxNodes), cancellationToken);
                string text = AxSnapshotWriter.Write(tree).Text;
                if (read > 0 && text == previous) break;
                previous = text;
            }
        }
        catch (DesktopWindowGoneException)
        {
            // 动作把窗口关了（点了关闭、保存后关窗）：如实说，它的编号作废
            state.Tree = null;
            state.Refs = new Dictionary<string, AxNode>();
            return new DesktopView(window, string.Empty, string.Empty,
                [.. notes, $"{window.Id} closed after that step. Call {DesktopTools.WindowsMention} to see what is open now."]);
        }

        return await ComposeAsync(window, state, tree!, notes, EDesktopView.Outline, cancellationToken);
    }

    private (double X, double Y) PointOf(DesktopWindow window, Target target, bool allowWindowCenter)
    {
        if (target.Point is { } point) return point;
        if (target.Node?.Bounds is { IsEmpty: false } bounds) return (bounds.CenterX, bounds.CenterY);
        if (target.Node == null && allowWindowCenter) return (window.Info.Bounds.CenterX, window.Info.Bounds.CenterY);
        throw new AxActionException($"{target.Node?.Label} has no position on screen, so it can't be clicked. " +
                                    "Scroll it into view or take a new snapshot.");
    }

    private static AxNode Required(AxNode? node, DesktopActionRequest request) =>
        node ?? throw new AxActionException($"{request.Action.ToString().ToLowerInvariant()} needs a target: a ref from the latest snapshot.");

    // 密码只经用户的手：目标是密码框，或没给目标而焦点在密码框里
    private static void RefuseSecret(AxNode? node, WindowState state, DesktopActionRequest request)
    {
        bool secret = node?.Secret ?? (state.Tree != null && FocusedSecret(state.Tree));
        if (secret)
        {
            throw new AxActionException("That is a password field: passwords are typed only by the user. " +
                                        $"Use {DesktopTools.HandoffMention} to ask them.");
        }
    }

    private static bool FocusedSecret(AxNode node) => FocusedNode(node) is { Secret: true };

    private static AxNode? FocusedNode(AxNode node) =>
        node.Focused ? node : node.Children.Select(FocusedNode).FirstOrDefault(found => found != null);

    private static bool IsRef(string key) => key.Length > 1 && key[0] == 'e' && key[1..].All(char.IsAsciiDigit);

    private static Task<bool> OnPlatformAsync(Func<bool> work) => Task.Run(work);

    // 第 1 级：图里认出的字（ocr-text）背后没有无障碍元素，直接交给按位置发事件
    private Task<bool> AccessibleAsync(AxNode node, EDesktopAction action, string value) =>
        node.Source == null ? Task.FromResult(false) : OnPlatformAsync(() => _platform!.TryPerform(node, action, value));

    [GeneratedRegex(@"^\s*\(?\s*(\d+(?:\.\d+)?)\s*,\s*(\d+(?:\.\d+)?)\s*\)?\s*$")]
    private static partial Regex CoordinatesPattern();

    private readonly record struct Target(AxNode? Node, (double X, double Y)? Point);
}

/// <summary>
/// 一个窗口动作
/// </summary>
/// <param name="Window">窗口编号、应用名或标题里的字</param>
/// <param name="Action">做什么</param>
/// <param name="Target">对谁：快照里的编号、看得见的文字，或窗口内坐标「x,y」；整窗动作为空</param>
/// <param name="Value">填的字、选项、按键名、滚动方向</param>
/// <param name="Real">是否用真实键鼠（第 3 级，审批时总要用户点）</param>
public sealed record DesktopActionRequest(string Window, EDesktopAction Action, string Target, string Value, bool Real);
