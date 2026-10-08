using System.Runtime.InteropServices;
using CliWrap;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Process;

/// <summary>
/// 让拉起的子进程随本进程一起死：崩溃、被强杀也不留残留。
/// Windows 进一个「句柄关了就杀光」的作业对象，由内核保证；macOS / Linux 经 /bin/sh 看门——
/// 它的 stdin 接在本进程持有的管道上，本进程一死写端被内核关掉，看门的读到 EOF 就杀掉子进程，
/// 子进程自己退出时看门的也退，退出码原样带回。不用 Linux 的 PDEATHSIG：它跟的是创建子进程的线程，线程池线程一退就误杀。
/// </summary>
public static class ChildProcessBinding
{
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint KillOnJobClose = 0x2000;

    // fd 3 留着读生命线；子进程的 stdin 接 /dev/null，不碰管道
    private const string WatchScript = """
                                       exec 3<&0
                                       "$@" </dev/null 3<&- &
                                       child=$!
                                       trap 'kill "$child" 2>/dev/null' TERM INT HUP
                                       ( while read -r line <&3; do :; done; kill "$child" 2>/dev/null ) </dev/null &
                                       watcher=$!
                                       exec 3<&-
                                       wait "$child"
                                       code=$?
                                       kill "$watcher" 2>/dev/null
                                       exit $code
                                       """;

    private static readonly Lazy<IntPtr> Job = new(CreateKillOnCloseJob);

    /// <summary>
    /// 生命线：一直不写也不结束，本进程活着管道就一直开着
    /// </summary>
    public static PipeSource Lifeline { get; } = PipeSource.Create(async (_, cancellationToken) =>
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// 包成「随本进程退出」的命令
    /// </summary>
    /// <param name="executablePath">可执行文件</param>
    /// <param name="arguments">参数（逐项，不必加引号）</param>
    /// <param name="lifeline">生命线；null 用 <see cref="Lifeline"/>（测试传一个会结束的来模拟本进程死掉）</param>
    /// <returns>命令；环境变量、校验等由调用方再加</returns>
    public static Command Wrap(string executablePath, IReadOnlyList<string> arguments, PipeSource? lifeline = null)
    {
        if (OperatingSystem.IsWindows()) return Cli.Wrap(executablePath).WithArguments(arguments);
        return Cli.Wrap("/bin/sh")
            .WithArguments(["-c", WatchScript, "uiharu-child", executablePath, ..arguments])
            .WithStandardInputPipe(lifeline ?? Lifeline);
    }

    /// <summary>
    /// 子进程起来后调用：Windows 上把它放进作业对象，其余平台什么都不做
    /// </summary>
    /// <param name="processId">进程 Id</param>
    public static void OnStarted(int processId)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
            if (Job.Value == IntPtr.Zero || !AssignProcessToJobObject(Job.Value, process.Handle))
                Log.Warning($"Bind child process {processId} to job failed: {Marshal.GetLastWin32Error()}");
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            //起来就退了
        }
    }

    // 句柄随本进程关闭时内核杀掉作业里所有进程；句柄故意不关
    private static IntPtr CreateKillOnCloseJob()
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;
        JobExtendedLimitInformation info = new() { BasicLimitInformation = { LimitFlags = KillOnJobClose } };
        if (SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, ref info,
                (uint)Marshal.SizeOf<JobExtendedLimitInformation>())) return job;
        Log.Warning($"Create kill-on-close job failed: {Marshal.GetLastWin32Error()}");
        return IntPtr.Zero;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass,
        ref JobExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobExtendedLimitInformation
    {
        public JobBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
