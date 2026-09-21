/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 路径最近记录的公共实现：工作区历史、搜索目录历史、文本文件历史都是
/// 「置顶 + 按路径去重 + 裁尾 + 落盘」，之前各写一份。
///
/// 只管<b>列表操作</b>：「存不存在才记」这类校验留给调用方
/// （工作区要求目录存在、文本文件要求文件存在、语义各不相同），
/// 「什么时候落盘」也留给调用方（配置走 <c>Save()</c>、搜索走 <c>SaveUtility</c>）。
/// 操作直接改调用方传进来的那份 <see cref="IList{T}"/>（配置反序列化后的那份），
/// 不另起镜像——镜像就要双向同步，漏一边就静默分叉。
/// </summary>
public sealed class RecentPathList
{
    private readonly List<string> _storage;

    /// <summary>条数上限</summary>
    public int Limit { get; }

    /// <summary>当前记录（最新在前），只读视图</summary>
    public IReadOnlyList<string> Items => _storage;

    /// <param name="storage">调用方持有的那份列表（配置属性或已加载的历史），原地修改</param>
    /// <param name="limit">条数上限，小于 1 按 1 算</param>
    public RecentPathList(List<string> storage, int limit = 10)
    {
        _storage = storage;
        Limit = Math.Max(1, limit);
    }

    /// <summary>
    /// 记一次使用：归一化后置顶、去重、裁掉超限尾部
    /// </summary>
    /// <param name="path">路径；空或非法返回 false</param>
    /// <returns>列表是否发生变化（调用方据此决定落不落盘）</returns>
    public bool Remember(string? path)
    {
        string? full = Normalize(path);
        if (full == null) return false;
        if (_storage.Count > 0 && string.Equals(_storage[0], full, StringComparison.Ordinal)) return false;

        _storage.RemoveAll(x => string.Equals(Normalize(x), full, StringComparison.Ordinal));
        _storage.Insert(0, full);
        while (_storage.Count > Limit) _storage.RemoveAt(_storage.Count - 1);
        return true;
    }

    /// <summary>
    /// 移除一条记录
    /// </summary>
    /// <returns>是否真的删掉了东西</returns>
    public bool Forget(string? path)
    {
        string? full = Normalize(path);
        if (full == null) return false;
        return _storage.RemoveAll(x => string.Equals(Normalize(x), full, StringComparison.Ordinal)) > 0;
    }

    /// <summary>剔除已不存在的路径（文件和目录都算存在）</summary>
    /// <returns>是否真的删掉了东西</returns>
    public bool PruneMissing()
    {
        return _storage.RemoveAll(x => !File.Exists(x) && !Directory.Exists(x)) > 0;
    }

    /// <summary>清空全部记录</summary>
    /// <returns>之前是否非空</returns>
    public bool Clear()
    {
        if (_storage.Count == 0) return false;
        _storage.Clear();
        return true;
    }

    /// <summary>归一化：去空白转绝对路径，非法字符时退回 trim 原样（不丢记录）</summary>
    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string trimmed = path.Trim();
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception)
        {
            return trimmed;
        }
    }
}
