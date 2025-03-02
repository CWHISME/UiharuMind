using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Memery;

public class MemoryManager : UniquieContainerSingleton<MemoryManager, MemoryData>
{
    protected override string SaveRootPath => SettingConfig.MemoryDataPath;

    protected override void OnOrderedItems(List<MemoryData> items)
    {
        items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }

    public MemoryData? GetMemoryData(string name)
    {
        return ItemDictionary.GetValueOrDefault(name);
    }

    public bool TryGetMemoryData(string name, out MemoryData? memoryData)
    {
        memoryData = ItemDictionary.GetValueOrDefault(name);
        return memoryData != null;
    }
}