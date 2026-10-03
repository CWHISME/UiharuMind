using System;
using System.IO;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core;

/// <summary>
/// 存在即为真的标记文件开关：刻意不走常规配置，不进设置页，普通用户看不到；打开一次之后重启仍生效
/// </summary>
public sealed class MarkerFileFlag
{
    private readonly string _path;
    private bool? _value;

    /// <param name="path">标记文件路径</param>
    public MarkerFileFlag(string path)
    {
        _path = path;
    }

    /// <summary>开关变化时触发</summary>
    public event Action? Changed;

    /// <summary>此刻是否打开（首次访问读一次标记文件）</summary>
    public bool IsSet => _value ??= File.Exists(_path);

    /// <summary>
    /// 打开 / 关闭并落盘
    /// </summary>
    /// <param name="value">True 打开</param>
    public void Set(bool value)
    {
        if (IsSet == value) return;

        _value = value;
        try
        {
            if (value)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, DateTimeOffset.Now.ToString("O"));
            }
            else if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Toggle marker '{Path.GetFileName(_path)}' failed: {e.Message}");
        }

        Changed?.Invoke();
    }
}
