/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 设置项的写回闸门：用户改一项就落盘，但从配置回填界面的那段不落盘。
///
/// 「回填期间别存」这件事此前有两种写法：一种是构造里绕过属性直接写 backing field，
/// 让生成的 <c>OnXChanged</c> 压根不触发——省事，但那是个看不见的约定，
/// 有人照常用属性赋值就会在打开设置页的瞬间把配置文件重写一遍；
/// 另一种是各自带个 <c>_isInitialized</c> 之类的布尔。
/// 这里把两种都换成一个显式作用域：回填包在 <see cref="BeginLoad"/> 里，闸门自己知道该不该落盘。
///
/// 只管「此刻该不该存」，不管「存什么」——字段怎么写回仍由各设置自己决定。
/// </summary>
public sealed class SettingsWriteBack
{
    private readonly Action _save; //真正落盘的动作
    private int _loadDepth; //>0 表示正在回填界面，此时不落盘

    /// <summary>
    /// 建一个写回闸门
    /// </summary>
    /// <param name="save">落盘动作，通常是某份 config 的 <c>Save()</c>。延迟调用，构造时不碰配置</param>
    public SettingsWriteBack(Action save)
    {
        ArgumentNullException.ThrowIfNull(save);
        _save = save;
    }

    /// <summary>是否正处在回填作用域内。此时写回一律静默</summary>
    public bool IsLoading => _loadDepth > 0;

    /// <summary>
    /// 进入回填作用域：期间的 <see cref="Save"/> 全部静默。
    /// 可嵌套，最外层退出才恢复落盘；同一凭据重复释放只算一次。
    /// </summary>
    /// <returns>退出作用域的凭据，用 <c>using</c> 持有</returns>
    public IDisposable BeginLoad()
    {
        _loadDepth++;
        return new LoadScope(this);
    }

    /// <summary>落盘。回填期间什么也不做</summary>
    public void Save()
    {
        if (_loadDepth > 0) return;
        _save();
    }

    private sealed class LoadScope : IDisposable
    {
        private SettingsWriteBack? _owner;

        public LoadScope(SettingsWriteBack owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_owner == null) return; //重复 Dispose 不该把深度扣穿
            _owner._loadDepth--;
            _owner = null;
        }
    }
}
