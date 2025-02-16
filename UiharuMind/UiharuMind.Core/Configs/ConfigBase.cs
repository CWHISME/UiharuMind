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
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using UiharuMind.Core.Core.Attributes;

namespace UiharuMind.Core.Core.Configs;

public class ConfigBase : INotifyPropertyChanged
{
    [SettingConfigIgnoreDisplay]
    [SettingConfigIgnoreValue]
    [JsonIgnore]
    public bool IsDirty { get; set; } = false;

    public void Save()
    {
        SaveUtility.SaveRootFile(this.GetType().Name, this);
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