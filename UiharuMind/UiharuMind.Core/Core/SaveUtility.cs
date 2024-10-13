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

using System.Text.Json;
using System.Text.Json.Serialization;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core;

public static class SaveUtility
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // 忽略 JSON 中的 null 值
        AllowTrailingCommas = true, // 允许尾随逗号
        ReadCommentHandling = JsonCommentHandling.Skip, // 忽略注释
        PropertyNameCaseInsensitive = true, // 属性名称不区分大小写
        UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode // 忽略未知类型
    };

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
        SaveToPath(Path.Combine(SettingConfig.SaveDataPath, fileName), target);
    }

    public static void SaveToPath(string filePath, object target)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (dir == null) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, JsonSerializer.Serialize(target, _options));
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    public static T Load<T>(Type t) where T : new()
    {
        return Load<T>(t.Name);
    }

    public static T Load<T>(string fileName) where T : new()
    {
        string path = Path.Combine(SettingConfig.SaveDataPath, fileName);
        if (File.Exists(path)) return LoadFromString<T>(File.ReadAllText(path));
        return new T();
    }

    public static T LoadFromString<T>(string jsonString) where T : new()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(jsonString, _options) ?? new T();
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }

        return new T();
    }

    /// <summary>
    /// 保存聊天记录
    /// </summary>
    public static void SaveChat(ChatSession chatSession)
    {
        if (!Directory.Exists(SettingConfig.SaveChatDataPath))
            Directory.CreateDirectory(SettingConfig.SaveChatDataPath);
        string fileName = $"{chatSession.LastTime}.json";
        //chatSession.LastTime.ToString("yyyy_MM_dd_HHmmss") + ".json";
        Save(Path.Combine(SettingConfig.SaveChatDataPath, fileName), chatSession);
    }

    /// <summary>
    /// 重新加载所有聊天记录
    /// </summary>
    /// <returns></returns>
    public static List<ChatSession> LoadChatHistory()
    {
        List<ChatSession> chatSessions = new List<ChatSession>();
        if (!Directory.Exists(SettingConfig.SaveChatDataPath))
            Directory.CreateDirectory(SettingConfig.SaveChatDataPath);
        foreach (string file in Directory.GetFiles(SettingConfig.SaveChatDataPath, "*.json"))
        {
            string json = File.ReadAllText(file);
            try
            {
                ChatSession? chatSession = JsonSerializer.Deserialize<ChatSession>(json);
                if (chatSession != null) chatSessions.Add(chatSession);
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
            }
        }

        return chatSessions;
    }
}