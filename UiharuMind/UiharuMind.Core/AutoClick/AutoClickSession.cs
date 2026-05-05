using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AutoClick;

/// <summary>
/// 自动点击会话数据
/// </summary>
public class AutoClickSession : IUniquieContainerItem
{
    /// <summary>
    /// 会话名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreateTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTime LastTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 重复次数
    /// </summary>
    public int RepeatCount { get; set; } = 1;

    /// <summary>
    /// 默认延迟
    /// </summary>
    public int DefaultDelay { get; set; } = 100;

    /// <summary>
    /// 动作列表
    /// </summary>
    public List<AutoClickActionData> Actions { get; set; } = new();

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 保存会话到文件
    /// </summary>
    public void Save()
    {
        LastTime = DateTime.Now;
        string fileName = $"{Name}.json";
        SaveUtility.Save(System.IO.Path.Combine(SettingConfig.SaveAutoClickDataPath, fileName), this);
    }

    /// <summary>
    /// 从文件加载会话
    /// </summary>
    public static AutoClickSession? Load(string filePath)
    {
        return SaveUtility.Load<AutoClickSession>(filePath);
    }
}

/// <summary>
/// 自动点击动作数据
/// </summary>
public class AutoClickActionData
{
    [JsonPropertyName("actionType")] public string ActionType { get; set; } = "";

    [JsonPropertyName("description")] public string Description { get; set; } = "";

    [JsonPropertyName("delay")] public int Delay { get; set; }

    [JsonPropertyName("mouseButton")] public int? MouseButton { get; set; }

    [JsonPropertyName("mouseX")] public short MouseX { get; set; }

    [JsonPropertyName("mouseY")] public short MouseY { get; set; }

    [JsonPropertyName("keyCode")] public int? KeyCode { get; set; }

    [JsonPropertyName("text")] public string? Text { get; set; }
    
    /// <summary>
    /// 滚轮滚动量（正数向上，负数向下）
    /// </summary>
    [JsonPropertyName("wheelDelta")] public int? WheelDelta { get; set; }
    
    /// <summary>
    /// 按键/鼠标按住的持续时间（毫秒）
    /// </summary>
    [JsonPropertyName("duration")] public int? Duration { get; set; }
}