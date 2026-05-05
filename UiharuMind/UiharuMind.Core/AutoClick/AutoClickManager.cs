using System;
using System.Collections.Generic;
using System.IO;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AutoClick;

/// <summary>
/// 自动点击会话管理器
/// </summary>
public class AutoClickManager : UniquieContainerSingleton<AutoClickManager, AutoClickSession>
{
    /// <summary>
    /// 创建新的录制会话
    /// </summary>
    public AutoClickSession CreateNewSession(string name = "新录制")
    {
        var session = new AutoClickSession
        {
            Name = GetUniqueName(name),
            CreateTime = DateTime.Now,
            LastTime = DateTime.Now
        };
        Add(session);
        return session;
    }

    protected override string SaveRootPath => SettingConfig.SaveAutoClickDataPath;

    protected override void OnOrderedItems(List<AutoClickSession> items)
    {
        items.Sort((x, y) => y.LastTime.CompareTo(x.LastTime));
    }
}
