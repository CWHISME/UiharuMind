using System.Runtime.InteropServices;

namespace UiharuMind.Core.Input.Linux;

/// <summary>
/// Linux 输入子系统的原生底座：evdev 读取与 uinput 注入共用同一套 input_event 结构体和 libc 调用。
/// 常量取自内核 uapi/linux/input-event-codes.h 与 uapi/linux/uinput.h。
/// </summary>
internal static class LinuxInputNative
{
    public const ushort EvSyn = 0x00;
    public const ushort EvKey = 0x01;
    public const ushort EvRel = 0x02;
    public const ushort EvAbs = 0x03;

    public const ushort SynReport = 0x00;

    public const ushort RelX = 0x00;
    public const ushort RelY = 0x01;
    public const ushort RelWheel = 0x08;
    public const ushort RelHWheel = 0x06;

    public const ushort AbsX = 0x00;
    public const ushort AbsY = 0x01;

    public const ushort BtnLeft = 0x110;
    public const ushort BtnRight = 0x111;
    public const ushort BtnMiddle = 0x112;
    public const ushort BtnSide = 0x113;
    public const ushort BtnExtra = 0x114;

    /// <summary>ABS_CNT，uinput_user_dev 里四个量程数组的固定长度</summary>
    public const int AbsCount = 64;

    // _IOW('U', nr, int) —— 见 uapi/linux/uinput.h
    public const ulong UiSetEvBit = 0x40045564;
    public const ulong UiSetKeyBit = 0x40045565;
    public const ulong UiSetRelBit = 0x40045566;
    public const ulong UiSetAbsBit = 0x40045567;
    public const ulong UiDevCreate = 0x5501;
    public const ulong UiDevDestroy = 0x5502;

    public const int OpenReadOnly = 0x0000;
    public const int OpenWriteOnly = 0x0001;
    public const int OpenNonBlock = 0x0800;

    public const short PollIn = 0x0001;

    /// <summary>struct input_event（64 位下 timeval 为 2×long，共 24 字节）</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct InputEvent
    {
        public long TimeSeconds;
        public long TimeMicroseconds;
        public ushort Type;
        public ushort Code;
        public int Value;
    }

    /// <summary>struct pollfd</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short ReturnedEvents;
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    public static extern int Open(string path, int flags);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    public static extern int Close(int fd);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    public static extern nint Read(int fd, ref InputEvent buffer, nint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    public static extern nint Write(int fd, ref InputEvent buffer, nint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    public static extern nint Write(int fd, byte[] buffer, nint count);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static extern int Ioctl(int fd, ulong request, int value);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static extern int Ioctl(int fd, ulong request, nint value);

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    public static extern int Poll([In, Out] PollFd[] fds, uint count, int timeoutMilliseconds);
}
