using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Clipboard;

/// <summary>
/// Windows 上读剪贴板文本：<b>只走 OLE，独占一个 STA 线程，同一时刻只允许一个读者</b>。
///
/// <para>
/// <b>为什么不能直读。</b> 来源应用若用 OLE 延迟渲染（WPF 的 <c>SetDataObject(…, false)</c>、
/// JetBrains 这类，剪贴板上会出现 <c>DataObject</c> 与 <c>Ole Private Data</c> 两个格式），
/// <c>GetClipboardData</c> 会同步等对方把数据渲染出来。实测在 SourceTree 里复制一次，
/// 这一等是 <b>20 秒</b>；而这 20 秒里剪贴板被我们独占，来源应用连自己的收尾都做不了，
/// 于是两个应用一起卡死——这才是「复制一下卡好几秒」的真身，跟重试次数无关。
/// 所以直读这条路已经整个删掉了，连兜底都不留。
/// </para>
///
/// <para>
/// <b>OLE 为什么不卡。</b> <c>OleGetClipboard</c> 只短暂开合一次剪贴板拿到数据对象就还回去，
/// 真正的渲染发生在 <c>IDataObject.GetData</c> 里，此时我们<b>没有</b>持有剪贴板，
/// 来源应用该干嘛干嘛，慢也只慢我们自己这一条后台线程。
/// </para>
///
/// <para>
/// <b>STA 与消息循环不是可选项。</b> <c>OleGetClipboard</c> 要求线程先 <c>OleInitialize</c>，
/// 而 STA 线程必须泵消息，跨套间的 COM 回调才落得下来。
/// </para>
///
/// <para>
/// <b>为什么还要一个专门的线程。</b> 渲染慢时这里会阻塞，阻塞在池线程上就会堆出多个读者一起抢剪贴板；
/// 收成一条线程后，慢的时候后来的请求搭同一次的车，最坏也只是记录晚到，不会再叠加。
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsClipboardTextReader : IClipboardTextReader
{
    /// <summary>投给本线程自己队列的消息，用来把读者叫醒</summary>
    private const int WM_READ = 0x0400;

    /// <summary>等待一次读取的上限。超了只表示这次先不记，读者本身继续跑完（它已不占剪贴板）</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(2);

    /// <summary>剪贴板被别人开着时的重试次数。这个窗口通常几十毫秒，等一等就过去了</summary>
    private const int OleRetryCount = 3;

    /// <summary>第 n 次重试前等 n 倍，拉开间隔</summary>
    private const int OleRetryDelayMilliseconds = 40;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly ConcurrentQueue<TaskCompletionSource<string?>> _queue = new();
    private readonly object _gate = new();
    private Task<string?>? _inflight;
    private volatile bool _running = true;
    private uint _threadId;
    private bool _disposed;

    public WindowsClipboardTextReader()
    {
        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "ClipboardTextReader" };
#pragma warning disable CA1416
        _thread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
        _thread.Start();
    }

    public async Task<string?> ReadTextAsync()
    {
        if (_disposed) return null;
        if (!_ready.Wait(ReadyTimeout)) return null;

        Task<string?> read;
        lock (_gate)
        {
            // 已经有一次在读：搭它的车。读的这段时间里剪贴板再变，由调用方读完补一次兜住
            if (_inflight is { IsCompleted: false })
            {
                read = _inflight;
            }
            else
            {
                TaskCompletionSource<string?> completion =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);
                _inflight = completion.Task;
                _queue.Enqueue(completion);
                ClipboardWin32.PostThreadMessage(_threadId, WM_READ, IntPtr.Zero, IntPtr.Zero);
                read = completion.Task;
            }
        }

        Task finished = await Task.WhenAny(read, Task.Delay(ReadTimeout)).ConfigureAwait(false);
        return finished == read ? await read.ConfigureAwait(false) : null;
    }

    private void RunMessageLoop()
    {
        // 先把消息队列建出来，否则别的线程 PostThreadMessage 会失败
        ClipboardWin32.PeekMessage(out _, IntPtr.Zero, WM_READ, WM_READ, ClipboardWin32.PM_NOREMOVE);
        _threadId = ClipboardWin32.GetCurrentThreadId();

        int oleResult = ClipboardWin32.OleInitialize(IntPtr.Zero);
        if (oleResult < 0) Log.Warning($"OleInitialize failed: 0x{oleResult:X8}");
        _ready.Set();

        while (_running && ClipboardWin32.GetMessage(out ClipboardWin32.Msg message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.message == WM_READ) DrainQueue();
        }

        if (oleResult >= 0) ClipboardWin32.OleUninitialize();
    }

    private void DrainQueue()
    {
        while (_queue.TryDequeue(out TaskCompletionSource<string?>? completion))
        {
            string? text;
            try
            {
                text = ReadOnce();
            }
            catch (Exception e)
            {
                Log.Warning($"Read clipboard failed: {e.Message}");
                text = null;
            }

            lock (_gate) _inflight = null;
            completion.TrySetResult(text);
        }
    }

    /// <summary>
    /// 读一次。<b>只有 OLE 一条路</b>，没有直读兜底。
    ///
    /// <para>
    /// 直读要在持有全局剪贴板的情况下等来源应用渲染（延迟渲染），那几十秒里谁都别想碰剪贴板——
    /// 正是「复制一下把别的程序卡住」的来源。而 <c>OleGetClipboard</c> 对裸 <c>SetClipboardData</c>
    /// 放上去的数据同样会合成一个 data object，直读没有它能读、我们读不到的东西：Avalonia 自己的
    /// 剪贴板读取就只有 OLE 这一条路，记事本复制的东西它照样读得到。
    /// </para>
    /// </summary>
    private static string? ReadOnce()
    {
        int result = 0;
        for (int attempt = 0; attempt < OleRetryCount; attempt++)
        {
            if (attempt > 0) Thread.Sleep(OleRetryDelayMilliseconds * attempt);
            result = TryReadViaOle(out string? text);
            if (result == 0) return text;
            // 只有「剪贴板被别人开着」值得再等一次；别的错重试也是一样的结果
            if (result != ClipboardWin32.ClipboardCantOpen) break;
        }

        // 只有被占用才报是谁占着；别的错（比如 OLE 没初始化）跟别人无关
        string holder = result == ClipboardWin32.ClipboardCantOpen ? ClipboardWin32.DescribeHolder() : string.Empty;
        Log.Debug($"Read clipboard skipped: 0x{result:X8}{holder}");
        return null;
    }

    /// <summary>
    /// 经 OLE 取文本。<b>全程不持有剪贴板</b>，所以来源应用渲染得再慢也冻不住它
    /// </summary>
    /// <param name="text">读到的文本；剪贴板上没有文本时为 null</param>
    /// <returns><c>OleGetClipboard</c> 的返回值，0 表示这次拿到了剪贴板</returns>
    private static int TryReadViaOle(out string? text)
    {
        text = null;
        int result = ClipboardWin32.OleGetClipboard(out IntPtr unknown);
        if (result != 0) return result;
        if (unknown == IntPtr.Zero) return -1;

        IDataObject? dataObject = null;
        try
        {
            dataObject = (IDataObject)Marshal.GetTypedObjectForIUnknown(unknown, typeof(IDataObject));

            FORMATETC format = new()
            {
                cfFormat = (short)ClipboardWin32.CF_UNICODETEXT,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                ptd = IntPtr.Zero,
                tymed = TYMED.TYMED_HGLOBAL
            };

            dataObject.GetData(ref format, out STGMEDIUM medium);
            try
            {
                if (medium.tymed != TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero) return 0;
                IntPtr pointer = ClipboardWin32.GlobalLock(medium.unionmember);
                if (pointer == IntPtr.Zero) return 0;
                try
                {
                    text = Marshal.PtrToStringUni(pointer);
                }
                finally
                {
                    ClipboardWin32.GlobalUnlock(medium.unionmember);
                }
            }
            finally
            {
                ClipboardWin32.ReleaseStgMedium(ref medium);
            }
        }
        catch (Exception e)
        {
            // 拿不到格式是常态（复制的是文件或图片），不当错误刷屏
            Log.Debug($"Read clipboard via OLE failed: {e.Message}");
        }
        finally
        {
            // OleGetClipboard 给的那一份引用要还，RCW 自己那一份也要还
            Marshal.Release(unknown);
            if (dataObject != null) Marshal.ReleaseComObject(dataObject);
        }

        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;

        // WM_QUIT 让 GetMessage 返回 0，循环退出；线程还没起来时 _threadId 为 0，投了也无妨
        if (_threadId != 0) ClipboardWin32.PostThreadMessage(_threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        if (_thread.Join(ReadyTimeout)) _ready.Dispose();
        GC.SuppressFinalize(this);
    }
}
