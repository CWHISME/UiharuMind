using Microsoft.KernelMemory;
using Microsoft.KernelMemory.DocumentStorage.DevTools;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.KnowledgeBase;

public class MemoryManager : Singleton<MemoryManager>, IInitialize
{
    public readonly Dictionary<string, MemoryData> KnowledgeBasesDictionary =
        new Dictionary<string, MemoryData>();

    public void OnInitialize()
    {
        var files = Directory.Exists(SettingConfig.MemoryPath)
            ? Directory.GetFiles(SettingConfig.MemoryPath, "*.json", SearchOption.AllDirectories)
            : null;

        if (files != null)
        {
            foreach (var file in files)
            {
                var characterData = SaveUtility.Load<MemoryData>(file);
                if (characterData != null)
                {
                    if (string.IsNullOrEmpty(characterData.Name))
                        characterData.Name = Path.GetFileNameWithoutExtension(file);
                    KnowledgeBasesDictionary.Add(characterData.Name, characterData);
                }
            }
        }
    }

    public MemoryData? GetMemoryDatae(string name)
    {
        return KnowledgeBasesDictionary.GetValueOrDefault(name);
    }
}