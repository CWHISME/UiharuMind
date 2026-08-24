using System.Text;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Input.Linux;

/// <summary>
/// 一台通过 /dev/uinput 创建的虚拟输入设备。
/// 用的是 uinput_user_dev + UI_DEV_CREATE 的传统建设路径，而非 UI_DEV_SETUP/UI_ABS_SETUP：
/// 传统路径只需写一个定长结构体，无需再声明两套 ioctl 结构体，且内核长期兼容。
/// </summary>
internal sealed class UInputDevice : IDisposable
{
    /// uinput_user_dev 的定长布局，偏移见 uapi/linux/uinput.h
    private const int UserDevSize = 1116;
    private const int NameOffset = 0;
    private const int NameCapacity = 80;
    private const int BusTypeOffset = 80;
    private const int AbsMaxOffset = 92;
    private const int AbsMinOffset = 348;
    private const int BusVirtual = 0x06;

    /// 设备创建后 libinput 需要一点时间完成枚举，此前写入的事件会被丢弃
    private const int DeviceSettleMilliseconds = 200;

    private readonly object _writeLock = new();
    private int _fd = -1;

    private UInputDevice(int fd)
    {
        _fd = fd;
    }

    public bool IsValid => _fd >= 0;

    /// <summary>
    /// 创建一台虚拟设备
    /// </summary>
    /// <param name="name">设备名，会出现在 /proc/bus/input/devices 中</param>
    /// <param name="keyCodes">要声明的 KEY_*/BTN_* 集合，为空则不启用 EV_KEY</param>
    /// <param name="relativeAxes">要声明的 REL_* 集合</param>
    /// <param name="absoluteAxes">要声明的 ABS_* 集合及其量程上限</param>
    /// <returns>创建成功返回设备实例，否则返回 null</returns>
    public static UInputDevice? TryCreate(
        string name,
        IEnumerable<ushort> keyCodes,
        IEnumerable<ushort> relativeAxes,
        IReadOnlyDictionary<ushort, int>? absoluteAxes = null)
    {
        int fd = LinuxInputNative.Open(LinuxInputCapabilities.UinputDevicePath,
            LinuxInputNative.OpenWriteOnly | LinuxInputNative.OpenNonBlock);
        if (fd < 0)
        {
            Log.Warning($"无法打开 {LinuxInputCapabilities.UinputDevicePath}，输入模拟不可用。");
            return null;
        }

        try
        {
            var keys = keyCodes.ToArray();
            if (keys.Length > 0)
            {
                LinuxInputNative.Ioctl(fd, LinuxInputNative.UiSetEvBit, LinuxInputNative.EvKey);
                foreach (var key in keys)
                {
                    LinuxInputNative.Ioctl(fd, LinuxInputNative.UiSetKeyBit, key);
                }
            }

            var axes = relativeAxes.ToArray();
            if (axes.Length > 0)
            {
                LinuxInputNative.Ioctl(fd, LinuxInputNative.UiSetEvBit, LinuxInputNative.EvRel);
                foreach (var axis in axes)
                {
                    LinuxInputNative.Ioctl(fd, LinuxInputNative.UiSetRelBit, axis);
                }
            }

            if (absoluteAxes is { Count: > 0 })
            {
                LinuxInputNative.Ioctl(fd, LinuxInputNative.UiSetEvBit, LinuxInputNative.EvAbs);
                foreach (var axis in absoluteAxes.Keys)
                {
                    LinuxInputNative.Ioctl(fd, LinuxInputNative.UiSetAbsBit, axis);
                }
            }

            var descriptor = BuildUserDev(name, absoluteAxes);
            if (LinuxInputNative.Write(fd, descriptor, UserDevSize) != UserDevSize)
            {
                Log.Warning($"写入 uinput 设备描述失败：{name}");
                LinuxInputNative.Close(fd);
                return null;
            }

            if (LinuxInputNative.Ioctl(fd, LinuxInputNative.UiDevCreate, 0) < 0)
            {
                Log.Warning($"UI_DEV_CREATE 失败：{name}");
                LinuxInputNative.Close(fd);
                return null;
            }

            Thread.Sleep(DeviceSettleMilliseconds);
            return new UInputDevice(fd);
        }
        catch (Exception e)
        {
            Log.Error(e);
            LinuxInputNative.Close(fd);
            return null;
        }
    }

    private static byte[] BuildUserDev(string name, IReadOnlyDictionary<ushort, int>? absoluteAxes)
    {
        var buffer = new byte[UserDevSize];
        var encoded = Encoding.UTF8.GetBytes(name);
        Array.Copy(encoded, 0, buffer, NameOffset, Math.Min(encoded.Length, NameCapacity - 1));
        BitConverter.TryWriteBytes(buffer.AsSpan(BusTypeOffset), (ushort)BusVirtual);

        if (absoluteAxes == null) return buffer;

        foreach (var axis in absoluteAxes)
        {
            if (axis.Key >= LinuxInputNative.AbsCount) continue;
            BitConverter.TryWriteBytes(buffer.AsSpan(AbsMaxOffset + axis.Key * sizeof(int)), axis.Value);
            BitConverter.TryWriteBytes(buffer.AsSpan(AbsMinOffset + axis.Key * sizeof(int)), 0);
        }

        return buffer;
    }

    /// <summary>
    /// 写入一个输入事件（不含同步）
    /// </summary>
    /// <param name="type">EV_* 类型</param>
    /// <param name="code">事件码</param>
    /// <param name="value">事件值</param>
    public void Emit(ushort type, ushort code, int value)
    {
        lock (_writeLock)
        {
            if (_fd < 0) return;
            var inputEvent = new LinuxInputNative.InputEvent { Type = type, Code = code, Value = value };
            int size = System.Runtime.InteropServices.Marshal.SizeOf<LinuxInputNative.InputEvent>();
            LinuxInputNative.Write(_fd, ref inputEvent, size);
        }
    }

    /// <summary>
    /// 提交一次输入报告，此前 Emit 的事件到此才对下游生效
    /// </summary>
    public void Sync()
    {
        Emit(LinuxInputNative.EvSyn, LinuxInputNative.SynReport, 0);
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_fd < 0) return;
            LinuxInputNative.Ioctl(_fd, LinuxInputNative.UiDevDestroy, 0);
            LinuxInputNative.Close(_fd);
            _fd = -1;
        }
    }
}
