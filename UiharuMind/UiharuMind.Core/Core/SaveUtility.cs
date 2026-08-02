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
    public static readonly JsonSerializerOptions JsonOptions = new()
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
        Save(GetSaveDataPath(fileName), target);
    }

    public static void Save(string filePath, object target)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (dir == null) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            WriteAtomic(filePath, JsonSerializer.Serialize(target, JsonOptions));
        }
        catch (Exception e)
        {
            Log.Error($"Save File Error:{e.Message},Path:{filePath}");
        }
    }

    /// <summary>
    /// 原子写盘:先写临时文件再替换。进程死在写一半时,
    /// 目标文件仍是完整的旧版本,而不是半截损坏的 JSON。
    /// </summary>
    private static void WriteAtomic(string filePath, string content)
    {
        string tempPath = filePath + ".tmp";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, filePath, true);
    }

    /// <summary>
    /// 用指定序列化配置保存。会话存档需要 Microsoft.Extensions.AI 的 TypeInfoResolver
    /// 才能正确写入多态 AIContent，不能复用通用配置。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="target">对象</param>
    /// <param name="options">序列化配置</param>
    public static void Save(string filePath, object target, JsonSerializerOptions options)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (dir == null) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            WriteAtomic(filePath, JsonSerializer.Serialize(target, options));
        }
        catch (Exception e)
        {
            Log.Error($"Save File Error:{e.Message},Path:{filePath}");
        }
    }

    /// <summary>
    /// 用指定序列化配置读取。与通用 Load 不同，解析失败返回 null 而不是空对象——
    /// 调用方需要区分"文件不存在/已损坏"与"内容确实是空的"，才能决定是否走重建流程。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="options">序列化配置</param>
    /// <typeparam name="T">目标类型</typeparam>
    /// <returns>对象；文件缺失或解析失败为 null</returns>
    public static T? Load<T>(string filePath, JsonSerializerOptions options) where T : class
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(filePath), options);
        }
        catch (Exception e)
        {
            Log.Warning($"Load File Error:{e.Message},Path:{filePath}");
            return null;
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
        return JsonSerializer.Serialize(target, JsonOptions);
    }

    /// <summary>
    /// 根据保存名字获取完整的保存路径
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public static string GetSaveDataPath(string fileName)
    {
        return Path.Combine(SettingConfig.SaveSettingDataPath, fileName);
    }

    /// <summary>
    /// 获取完整的保存剪切板历史图片记录的路径
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public static string GetSaveClipboardHistoryImagePath(string fileName)
    {
        return Path.Combine(SettingConfig.SaveClipboardHistoryImagePath, fileName);
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
        return Load<T>(GetSaveDataPath(fileName));
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
            return JsonSerializer.Deserialize<T>(jsonString, JsonOptions) ?? new T();
        }
        catch (Exception e)
        {
            Log.Warning(e.ToString());
        }

        return new T();
    }

    public static object? LoadFromString(string jsonString, Type? type)
    {
        if (type == null) return null;
        try
        {
            return JsonSerializer.Deserialize(jsonString, type, JsonOptions) ?? null;
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }

        return null;
    }
}