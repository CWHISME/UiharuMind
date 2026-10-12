/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Core.AI.Execution.Tools.Desktop.Mac;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Tools.Desktop;

/// <summary>
/// 桌面宿主（ADR 0084）：窗口编号、每个会话看到的快照、拒绝名单、并发与三级动作的升级都在这里，
/// 平台差异关在 <see cref="IDesktopPlatform"/> 后面。全应用一个（<see cref="Shared"/>）：窗口编号是全局的，
/// 审批卡与风险分级按编号认应用，不必知道是哪个会话。
/// 这里管窗口与快照，动作在 <c>DesktopHost.Actions.cs</c>。
/// </summary>
public sealed partial class DesktopHost
{
    private const int MaxNodes = 1500; //一个应用的树动辄上万节点；超出的列成「还有几项」

    private static Func<IDesktopPlatform?> _platformFactory = CreateDefaultPlatform;
    private static DesktopHost? _shared;

    private readonly IDesktopPlatform? _platform;
    private readonly Func<int, bool>? _ownBrowser;
    private readonly Lock _windowsGate = new();
    private readonly List<DesktopWindow> _windows = new(); //最近一次列出的窗口，编号跨次复用
    private readonly ConcurrentDictionary<(string Session, string Window), WindowState> _states = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _appGates = new(); //同一应用的读与动一个个来
    private int _lastWindowNumber;
    private int _accessRequested; //请系统弹过授权框没有

    /// <summary>
    /// 造一个宿主（测试用假平台）
    /// </summary>
    /// <param name="platform">平台层；这个系统还不支持为 null</param>
    /// <param name="ownBrowser">某进程是不是浏览器工具开的 Chrome</param>
    public DesktopHost(IDesktopPlatform? platform, Func<int, bool>? ownBrowser = null)
    {
        _platform = platform;
        _ownBrowser = ownBrowser;
    }

    /// <summary>全应用共用的一个</summary>
    public static DesktopHost Shared =>
        _shared ??= new DesktopHost(_platformFactory(), pid => PuppeteerBrowserHost.Shared.OwnsProcess(pid));

    /// <summary>动手前在目标上闪一下（界面挂上；没挂就不闪）：第 1、2 级不动光标，用户看不出点了哪</summary>
    public Action<AxRect>? Highlight { get; set; }

    /// <summary>
    /// 各会话名下拍过快照的窗口数（内存账观测口，只读；<see cref="ReleaseSession"/> 清完这条会降）
    /// </summary>
    public IReadOnlyDictionary<string, int> DesktopSnapshotStateCount =>
        _states.Keys.GroupBy(key => key.Session).ToDictionary(x => x.Key, x => x.Count());

    /// <summary>当前共记着几个应用的并发锁（观测口，只读；<see cref="ListWindowsAsync"/> 会清掉退出的 pid）</summary>
    public int AppGateCount => _appGates.Count;

    /// <summary>这个系统有没有平台层（没授权也算有）</summary>
    public bool IsSupported => _platform != null;

    /// <summary>此刻用不了的原因；能用为 null</summary>
    public string? Unavailable =>
        _platform == null ? "Desktop control is not supported on this operating system yet." : _platform.Unavailable;

    /// <summary>
    /// 换平台层（Windows 的实现在单独的程序集里，由组装入口在首次使用前挂上）
    /// </summary>
    /// <param name="factory">造平台层</param>
    public static void UsePlatform(Func<IDesktopPlatform?> factory)
    {
        _platformFactory = factory;
        _shared = null;
    }

    /// <summary>
    /// 请系统授权（macOS 的无障碍）
    /// </summary>
    public void RequestAccess() => _platform?.RequestAccess();

    /// <summary>
    /// 列出窗口并分配编号
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>窗口，前台的在前</returns>
    /// <exception cref="AxActionException">用不了（没授权）</exception>
    public async Task<IReadOnlyList<DesktopWindow>> ListWindowsAsync(CancellationToken cancellationToken)
    {
        IDesktopPlatform platform = Platform();
        IReadOnlyList<DesktopWindowInfo> found = await Task.Run(platform.ListWindows, cancellationToken);
        lock (_windowsGate)
        {
            List<DesktopWindow> listed = found.Select(info =>
            {
                DesktopWindow? known = _windows.FirstOrDefault(window => platform.SameWindow(window.Info, info));
                string id = known?.Id ?? "w" + ++_lastWindowNumber;
                return new DesktopWindow(id, info, DesktopPolicy.Blocked(info, _ownBrowser));
            }).ToList();

            // 关掉的窗口连同各会话里它的快照一起丢：快照里攥着原生句柄
            foreach (DesktopWindow gone in _windows.Where(old => listed.All(now => now.Id != old.Id)))
            {
                foreach ((string, string) key in _states.Keys.Where(key => key.Window == gone.Id)) _states.TryRemove(key, out _);
            }

            // 退出的应用把它的锁也丢：锁是按 pid 记的，pid 会被复用（macOS 有回绕），
            // 留着旧进程的锁会给新进程白排一次队——无害但语义脏。
            // 已知竞态：会话线程刚 ResolveAsync 拿到 window（pid 已不在活列表）尚未进 WithAppAsync，
            // 这行删掉它的 gate，随后 GetOrAdd 白建一个没人竞争的——下次 ListWindowsAsync 再清，自愈。
            HashSet<int> alive = listed.Select(window => window.Info.Pid).ToHashSet();
            foreach (int pid in _appGates.Keys.Where(pid => !alive.Contains(pid))) _appGates.TryRemove(pid, out _);

            _windows.Clear();
            _windows.AddRange(listed);
            return listed;
        }
    }

    /// <summary>
    /// 按编号、应用名或标题找最近列出的窗口，<b>不碰系统</b>：审批卡与风险分级据此认应用
    /// </summary>
    /// <param name="query">窗口编号（w3）、应用名或标题里的字</param>
    /// <returns>窗口；没列出过为 null</returns>
    public DesktopWindow? FindWindow(string query)
    {
        string wanted = query.Trim();
        if (wanted.Length == 0) return null;
        lock (_windowsGate)
        {
            return _windows.FirstOrDefault(window => string.Equals(window.Id, wanted, StringComparison.OrdinalIgnoreCase))
                   ?? _windows.FirstOrDefault(window =>
                       string.Equals(window.Info.App, wanted, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(window.Info.AppId, wanted, StringComparison.OrdinalIgnoreCase))
                   ?? _windows.FirstOrDefault(window =>
                       window.Info.Title.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 给一个窗口拍快照
    /// </summary>
    /// <param name="sessionId">会话 id（编号按会话记）</param>
    /// <param name="window">窗口编号、应用名或标题里的字</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="view">要不要看图：树太空时自动加 OCR 层；要截图时附上画了编号框的图</param>
    /// <returns>窗口此刻的样子</returns>
    /// <exception cref="AxActionException">找不到、在拒绝名单里、用不了</exception>
    public async Task<DesktopView> SnapshotAsync(string sessionId, string window, CancellationToken cancellationToken,
        EDesktopView view = EDesktopView.Outline)
    {
        DesktopWindow target = await ResolveAsync(window, refresh: true, cancellationToken);
        return await WithAppAsync(target, cancellationToken, async () =>
        {
            WindowState state = _states.GetOrAdd((sessionId, target.Id), _ => new WindowState());
            List<string> notes = new();
            if (target.Info.Minimized) notes.Add("The window is minimized; what it shows may be incomplete.");
            AxNode tree = await Task.Run(() => _platform!.ReadWindow(target.Info, MaxNodes), cancellationToken);
            return await ComposeAsync(target, state, tree, notes, view, cancellationToken);
        });
    }

    /// <summary>
    /// 会话删了：它名下各窗口拍过的快照（攥着原生句柄）一起丢。窗口关掉的清理在 <see cref="ListWindowsAsync"/>，两条路径互不顶替
    /// </summary>
    /// <param name="sessionId">被删会话的 id</param>
    public void ReleaseSession(string sessionId)
    {
        foreach ((string session, string _) key in _states.Keys.Where(key => key.Session == sessionId))
        {
            _states.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 按会话最近一份快照把目标翻回元素，<b>不碰系统</b>：审批卡据此说「在 记事本 点击按钮『保存』」
    /// </summary>
    /// <param name="sessionId">会话 id</param>
    /// <param name="window">窗口编号、应用名或标题里的字</param>
    /// <param name="target">编号或看得见的文字；空为整窗动作</param>
    /// <returns>预览；窗口没列出过为 null</returns>
    public DesktopElementPreview? Peek(string sessionId, string window, string target)
    {
        if (FindWindow(window) is not { } found) return null;
        AxNode? node = null;
        if (target.Trim().Length > 0 && _states.TryGetValue((sessionId, found.Id), out WindowState? state) && state.Tree != null)
        {
            string key = AxSnapshotSearch.NormalizeTarget(target);
            node = state.Refs.TryGetValue(key, out AxNode? byRef) ? byRef : AxSnapshotSearch.FindByText(state.Tree, key);
        }

        return new DesktopElementPreview(found.Info.App, found.Info.Title, node?.Role ?? string.Empty,
            node?.Name.Trim() ?? string.Empty, node?.Secret == true);
    }

    // 没授权时第一次被用到就请系统弹授权框（只弹一次，之后系统也不再弹），模型据报错去请用户点允许
    private IDesktopPlatform Platform()
    {
        if (Unavailable is not { } reason) return _platform!;
        if (_platform != null && Interlocked.Exchange(ref _accessRequested, 1) == 0) _platform.RequestAccess();
        throw new AxActionException(reason);
    }

    // 编号认得出就直接用，不必重列；应用名、标题先重列一遍再找（窗口可能刚开）
    private async Task<DesktopWindow> ResolveAsync(string query, bool refresh, CancellationToken cancellationToken)
    {
        Platform();
        DesktopWindow? found = FindWindow(query);
        if (found == null || refresh)
        {
            await ListWindowsAsync(cancellationToken);
            found = found != null ? FindWindow(found.Id) : FindWindow(query);
        }

        if (found == null)
        {
            throw new AxActionException($"No window matches \"{query}\". Call {DesktopTools.WindowsMention} to see the open windows.");
        }

        if (found.Blocked is { } reason)
        {
            throw new AxActionException($"{found.Id} ({found.Info.App}) can't be read or controlled: {reason}.");
        }

        return found;
    }

    // 同一应用的读与动排队
    private async Task<T> WithAppAsync<T>(DesktopWindow window, CancellationToken cancellationToken, Func<Task<T>> work)
    {
        SemaphoreSlim gate = _appGates.GetOrAdd(window.Info.Pid, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await work();
        }
        finally
        {
            gate.Release();
        }
    }

    // 读好的树（需要时添上图里认出的字）写成快照，记作这个会话在这个窗口上看到的最近一份
    private async Task<DesktopView> ComposeAsync(DesktopWindow window, WindowState state, AxNode tree,
        IReadOnlyList<string> notes, EDesktopView view, CancellationToken cancellationToken, byte[]? captured = null)
    {
        List<string> allNotes = [.. notes];
        (AxNode withText, byte[]? picture) = await AddPictureAsync(window, tree, view, allNotes, captured, cancellationToken);
        AxSnapshot snapshot = AxSnapshotWriter.Write(withText);
        state.Tree = withText;
        state.Refs = snapshot.Refs;
        state.Picture = picture;
        byte[]? marked = view == EDesktopView.Screenshot && picture != null ? Mark(picture, snapshot, window.Info.Bounds) : null;
        return new DesktopView(window, snapshot.Text, snapshot.CompactText, allNotes) { Picture = marked };
    }

    private static IDesktopPlatform? CreateDefaultPlatform() => PlatformUtils.IsMacOS ? new MacDesktopPlatform() : null;

    /// <summary>
    /// 一个会话在一个窗口上看到的最近一份快照
    /// </summary>
    private sealed class WindowState
    {
        public AxNode? Tree { get; set; }

        public IReadOnlyDictionary<string, AxNode> Refs { get; set; } = new Dictionary<string, AxNode>();

        public AxRoundTracker Round { get; } = new();

        public byte[]? Picture { get; set; } //那份快照截的窗口图：画出来的界面靠它判断动作后变没变
    }
}

/// <summary>
/// 编了号的窗口
/// </summary>
/// <param name="Id">编号（w3），全应用一份，跨次列出复用</param>
/// <param name="Info">平台给的窗口</param>
/// <param name="Blocked">不能碰的原因；能碰为 null</param>
public sealed record DesktopWindow(string Id, DesktopWindowInfo Info, string? Blocked);

/// <summary>
/// 窗口在某一刻的样子
/// </summary>
/// <param name="Window">窗口</param>
/// <param name="Snapshot">完整快照</param>
/// <param name="CompactSnapshot">精简版</param>
/// <param name="Notes">这一步顺带发生的事</param>
public sealed record DesktopView(DesktopWindow Window, string Snapshot, string CompactSnapshot, IReadOnlyList<string> Notes)
{
    /// <summary>画了编号框的窗口截图（JPEG）；没要截图为 null</summary>
    public byte[]? Picture { get; init; }
}

/// <summary>
/// 快照要不要看图
/// </summary>
public enum EDesktopView
{
    /// <summary>只读无障碍树；树太空（自绘界面）时自动加上图里认出的字</summary>
    Outline,
    /// <summary>总是加上图里认出的字（树里有、但某块区域是画出来的）</summary>
    Ocr,
    /// <summary>另附一张画了编号框的截图</summary>
    Screenshot,
}

/// <summary>
/// 一次桌面调用给人看的样子（审批卡用）
/// </summary>
/// <param name="App">应用名</param>
/// <param name="WindowTitle">窗口标题</param>
/// <param name="Role">目标的角色；没有目标或翻不出为空</param>
/// <param name="Name">目标的名字</param>
/// <param name="Secret">目标是否密码框</param>
public sealed record DesktopElementPreview(string App, string WindowTitle, string Role, string Name, bool Secret);
