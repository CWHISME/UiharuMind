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

using System.Text.Encodings.Web;
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
        UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode, // 忽略未知类型
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Save()
    {
        // File.WriteAllText("./SaveData/Setting.cfg", JsonSerializer.Serialize(Setting));
        // Save("Setting.cfg", Setting);
    }

    public static void Save(Type t, object target)
    {
        SaveRootFile(t.Name, target);
    }

    public static void SaveRootFile(string fileName, object target)
    {
        Save(Path.Combine(SettingConfig.SaveDataPath, fileName), target);
    }

    public static void Save(string filePath, object target)
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

    public static void Delete(string filePath)
    {
        try
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    public static string SaveToString(object target)
    {
        // JsonSerializer.SerializeAsync(target, _options)
        return JsonSerializer.Serialize(target, _options);
    }

    //=========================Load=================================

    public static T LoadOrNew<T>(Type t) where T : class, new()
    {
        return LoadRootFile<T>(t.Name) ?? new T();
    }

    public static T? Load<T>(Type t) where T : class, new()
    {
        return LoadRootFile<T>(t.Name);
    }

    public static T? LoadRootFile<T>(string fileName) where T : class, new()
    {
        string path = Path.Combine(SettingConfig.SaveDataPath, fileName);
        return Load<T>(path);
    }

    public static T? Load<T>(string filePath) where T : class, new()
    {
        if (File.Exists(filePath)) return LoadFromString<T>(File.ReadAllText(filePath));
        return null;
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

    public static object? LoadFromString(string jsonString, Type? type)
    {
        if (type == null) return null;
        try
        {
            return JsonSerializer.Deserialize(jsonString, type, _options) ?? null;
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }

        return null;
    }
}