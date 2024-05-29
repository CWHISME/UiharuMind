using System.Text.Json;

namespace UiharuMind.Core.Core;

public static class SaveUtility
{
    public static void Save()
    {
        // File.WriteAllText("./SaveData/Setting.cfg", JsonSerializer.Serialize(Setting));
        // Save("Setting.cfg", Setting);
    }

    public static void Save(Type t, object target)
    {
        Save(t.Name, target);
    }

    public static void Save(string fileName, object target)
    {
        if (!Directory.Exists(SettingConfig.SaveDataPath)) Directory.CreateDirectory(SettingConfig.SaveDataPath);
        File.WriteAllText(Path.Combine(SettingConfig.SaveDataPath, fileName), JsonSerializer.Serialize(target));
    }

    public static T Load<T>(Type t) where T : new()
    {
        return Load<T>(t.Name);
    }

    public static T Load<T>(string fileName) where T : new()
    {
        string path = Path.Combine(SettingConfig.SaveDataPath, fileName);
        if (File.Exists(path)) return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T();
        return new T();
    }
}