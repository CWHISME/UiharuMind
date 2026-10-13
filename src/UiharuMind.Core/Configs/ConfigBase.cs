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
        if (AllowSelfSave) SaveUtility.Save(AppPaths.Config.ForType(this.GetType().Name), this, SaveUtility.SaveJsonOptions);
        else Log.Error("Save is not allowed for this config: " + this.GetType().Name);
    }

    protected virtual bool AllowSelfSave => true;

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