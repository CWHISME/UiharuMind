/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Configs;

/// <summary>
/// 设置的基类。设置页按反射列出属性生成控件（见 SettingListView），所以类型上声明保留公开属性，AOT 裁剪也不会把它们的元数据去掉
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public class ConfigBase : INotifyPropertyChanged
{
    [SettingConfigIgnoreDisplay]
    [SettingConfigIgnoreValue]
    [JsonIgnore]
    public bool IsDirty { get; set; } = false;

    public void Save()
    {
        // 反序列化期间的 setter 不落盘：STJ 逐键调 setter 时对象还是半成品，
        // 此刻写盘会把它还沒读到的尾部字段覆盖成默认值（见 SaveUtility.SaveSuppressed）。
        if (SaveUtility.SaveSuppressed) return;
        if (AllowSelfSave) SaveUtility.Save(AppPaths.Config.ForType(this.GetType().Name), this, SaveUtility.SaveJsonOptions);
        else Log.Error("Save is not allowed for this config: " + this.GetType().Name);
    }

    protected virtual bool AllowSelfSave => true;

    /// <summary>
    /// 读档完成后的收尾：旧字段迁移、派生值修正这类"只在刚读完时跑一次"的事。
    /// 由 <see cref="TConfigBase{T}"/> 在反序列化结束、写盘抑制解开之后调用——
    /// 反序列化中途对象还是半成品，在那里做迁移会拿别的字段的默认值当真相。
    ///
    /// <b>约束：不许碰其他配置/单例、不许落盘、不许拿 UI 线程持有的锁。</b>
    /// 它跑在类型初始化的锁内（<c>Current</c> 的静态构造），而 <c>Current</c> 常常是
    /// 后台线程第一个碰（派活路径会读配置）：在这里引一个互相依赖的静态初始化就是死锁，
    /// 抛异常则是 <c>TypeInitializationException</c>——之后每次访问都炸，不可恢复。
    /// </summary>
    internal virtual void OnLoaded()
    {
    }

    // public void Load()
    // {
    //     SaveUtility.Load<T>(this.GetType().Name);
    // }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        IsDirty = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}