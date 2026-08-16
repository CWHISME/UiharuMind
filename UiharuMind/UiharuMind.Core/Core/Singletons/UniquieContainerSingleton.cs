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

using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Core.Singletons;

public interface IUniquieContainerItem
{
    string Name { get; set; }
}

public abstract class UniquieContainerSingleton<TMgr, T> : Singleton<TMgr>, IInitialize
    where T : class, IUniquieContainerItem, new() where TMgr : class, new()
{
    public readonly Dictionary<string, T> ItemDictionary =
        new Dictionary<string, T>();

    public event Action<T>? OnItemAdded;
    public event Action<T>? OnItemRemoved;

    protected virtual string DefaultItemName => "NewItem";
    protected abstract string SaveRootPath { get; }
    protected abstract void OnOrderedItems(List<T> items);

    public List<T> GetOrderedItems()
    {
        List<T> items = new List<T>(ItemDictionary.Values);
        OnOrderedItems(items);
        return items;
    }

    public virtual void OnInitialize()
    {
        ItemDictionary.Clear();

        if (!Directory.Exists(SaveRootPath)) return;
        foreach (string file in Directory.GetFiles(SaveRootPath, "*.json"))
        {
            var item = SaveUtility.Load<T>(file);
            if (item != null)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrEmpty(item.Name) || item.Name != fileName)
                    item.Name = fileName;
                ItemDictionary.Add(item.Name, item);
            }
        }
    }

    public T AddNewItem(string? name = null)
    {
        return Add(GetNewUniqueItem(name));
    }

    /// <summary>
    /// 添加,如果添加的session的名称已经存在，则会自动修改名称变成一个新的
    /// 由于 item 本身引用不变,因此如果不是自己创建的 item 不要直接调用,不然容易导致旧的名字也被修改了(因为同属一个引用)
    /// </summary>
    /// <param name="item"></param>
    public T Add(T item)
    {
        item.Name = GetUniqueName(item.Name);
        ItemDictionary[item.Name] = item;
        Save(item);
        OnItemAdded?.Invoke(item);
        return item;
    }

    /// <summary>
    /// 复制
    /// </summary>
    /// <param name="item"></param>
    public T Copy(T item)
    {
        var sessionNew = GameUtils.Copy(item);
        Add(sessionNew);
        return sessionNew;
    }

    /// <summary>
    /// 删除
    /// </summary>
    /// <param name="item"></param>
    public virtual void Delete(T item)
    {
        RemoveEntry(item);
    }

    /// <summary>
    /// 修改名称
    /// </summary>
    /// <param name="item"></param>
    /// <param name="newName">如果存在重复，返回被自动修改后的名称</param>
    public virtual string ModifyName(T item, string newName)
    {
        RemoveEntry(item);
        item.Name = newName;
        Add(item);
        return item.Name;
    }

    /// <summary>
    /// 摘掉条目并删掉它的 json。
    ///
    /// 刻意不走 <see cref="Delete"/>：子类可能在 Delete 里连带清掉条目的 side-car
    /// （<see cref="AI.Memory.MemoryManager"/> 就要删索引库），而改名同样要摘条目，
    /// 走 Delete 就会在改名途中把 side-car 删掉。
    /// </summary>
    private void RemoveEntry(T item)
    {
        ItemDictionary.Remove(item.Name);
        DeleteFile(item);
        OnItemRemoved?.Invoke(item);
    }

    /// <summary>
    /// 保存
    /// </summary>
    public void Save(T chatSession, bool isAsNew = false)
    {
        if (isAsNew)
        {
            Add(chatSession);
            return;
        }

        // if (!Directory.Exists(SettingConfig.SaveChatDataPath))
        //     Directory.CreateDirectory(SettingConfig.SaveChatDataPath);
        string fileName = $"{chatSession.Name}.json";
        //chatSession.LastTime.ToString("yyyy_MM_dd_HHmmss") + ".json";
        SaveUtility.Save(Path.Combine(SaveRootPath, fileName), chatSession);
    }

    /// <summary>
    /// 删除文件
    /// </summary>
    public void DeleteFile(T item)
    {
        // SaveUtility.Delete(Path.Combine(SettingConfig.SaveChatDataPath, fileName));
        DeleteFile(item.Name);
    }

    /// <summary>
    /// 删除文件
    /// </summary>
    public void DeleteFile(string name)
    {
        string fileName = $"{name}.json";
        SaveUtility.Delete(Path.Combine(SaveRootPath, fileName));
    }

    public T GetNewUniqueItem(string? name = null)
    {
        if (string.IsNullOrEmpty(name)) name = DefaultItemName;
        return new T() { Name = GetUniqueName(name) };
    }

    public string GetUniqueName(string targetName)
    {
        int i = 0;
        string uniqueName = targetName;
        // 防止重复
        while (ItemDictionary.ContainsKey(uniqueName))
        {
            uniqueName = targetName + "_" + i++;
        }

        return uniqueName;
    }
}
