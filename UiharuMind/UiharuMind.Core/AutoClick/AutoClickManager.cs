using System;
using System.Collections.Generic;
using System.IO;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AutoClick;

public class AutoClickManager : UniquieContainerSingleton<AutoClickManager, AutoClickSession>
{
    public AutoClickSession CreateNewSession(string name = "New Flow")
    {
        var session = new AutoClickSession
        {
            Version = AutoClickSession.CurrentVersion,
            Name = GetUniqueName(name),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        Add(session);
        return session;
    }

    protected override string SaveRootPath => SettingConfig.SaveAutoClickDataPath;

    protected override void OnOrderedItems(List<AutoClickSession> items)
    {
        items.Sort((x, y) => y.UpdatedAt.CompareTo(x.UpdatedAt));
    }

    public new AutoClickSession Add(AutoClickSession session)
    {
        session.Version = AutoClickSession.CurrentVersion;
        return base.Add(session);
    }

    public override void OnInitialize()
    {
        ItemDictionary.Clear();

        if (!Directory.Exists(SaveRootPath)) return;
        foreach (var file in Directory.GetFiles(SaveRootPath, "*.json"))
        {
            var item = SaveUtility.Load<AutoClickSession>(file);
            if (item == null || item.Version != AutoClickSession.CurrentVersion)
            {
                Log.Debug($"Ignore legacy AutoClick session: {file}");
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(item.Name) || item.Name != fileName)
            {
                item.Name = fileName;
            }

            ItemDictionary[item.Name] = item;
        }
    }

    public new void Save(AutoClickSession session, bool isAsNew = false)
    {
        session.Version = AutoClickSession.CurrentVersion;
        session.UpdatedAt = DateTime.Now;
        base.Save(session, isAsNew);
    }
}
