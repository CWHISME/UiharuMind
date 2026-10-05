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
using UiharuMind.Core.AI.Chat;
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
    /// 原子保存一段文本
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="content">文本内容</param>
    public static void SaveText(string filePath, string content)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (dir == null) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            WriteAtomic(filePath, content);
        }
        catch (Exception e)
        {
            Log.Error($"Save File Error:{e.Message},Path:{filePath}");
        }
    }

    /// <summary>
    /// 原子写盘:先写临时文件再替换。进程死在写一半时,
    /// 目标文件仍是完整的旧版本,而不是半截损坏的 JSON。
    /// 临时文件每次调用唯一:同一文件的并发保存曾共用 <c>filePath + ".tmp"</c>,
    /// 先替换的吃掉 tmp,后到的以 FileNotFound 炸掉并弹错误框。
    /// 唯一 tmp + 瞬态重试后,并发只剩"谁后写谁赢",落盘的永远是完整快照。
    /// </summary>
    private static void WriteAtomic(string filePath, string content)
    {
        string tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            const int maxAttempts = 3;
            for (int attempt = 1;; attempt++)
            {
                try
                {
                    File.WriteAllText(tempPath, content);
                    File.Move(tempPath, filePath, true);
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    // 同一目标的并发替换等瞬态失败:退避重试。
                    // 内存里的对象才是权威,下一次保存还会重写,这里只为吞掉这次抖动。
                    // 注意 FileNotFoundException 也是 IOException,正好覆盖"目标被并发替换删掉"那一下。
                    Thread.Sleep(TimeSpan.FromMilliseconds(20 * attempt));
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // 残留 tmp 不影响下次保存,下次会用新的唯一文件名
            }
        }
    }

    /// <summary>
    /// 以 UTF-8 流式原子写盘：序列化直接写进文件流，不经过中间字符串。
    /// 大列表（如会话索引）整份重写时，不会把整份 JSON 抬进大对象堆。
    /// 原子替换与并发语义同 <see cref="Save(string, object)"/>。
    /// </summary>
    /// <param name="filePath">目标路径</param>
    /// <param name="write">在打开的 writer 上写内容（writer 由本方法管理，调用方写完即回）</param>
    /// <param name="options">序列化配置；其中的 <c>WriteIndented</c> 与 <c>Encoder</c> 同时决定 writer 行为</param>
    /// <returns>是否成功落盘</returns>
    public static bool SaveUtf8(string filePath, Action<Utf8JsonWriter> write, JsonSerializerOptions options)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (dir == null) return false;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                const int maxAttempts = 3;
                for (int attempt = 1;; attempt++)
                {
                    try
                    {
                        using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions
                        {
                            Indented = options.WriteIndented,
                            // 传入现成 writer 时转义由 writer 自己的 Encoder 决定，options.Encoder 不生效；
                            // 不显式带上，非 ASCII（如中文标题）就会退化成 \uXXXX，文件膨胀且不可读
                            Encoder = options.Encoder,
                        }))
                        {
                            write(writer);
                            writer.Flush();
                            stream.Flush(flushToDisk: true);
                        }

                        File.Move(tempPath, filePath, true);
                        return true;
                    }
                    catch (IOException) when (attempt < maxAttempts)
                    {
                        Thread.Sleep(TimeSpan.FromMilliseconds(20 * attempt));
                    }
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch
                {
                    // 残留 tmp 不影响下次保存,下次会用新的唯一文件名
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Save File Error:{e.Message},Path:{filePath}");
            return false;
        }
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

    //=========================Load=================================

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