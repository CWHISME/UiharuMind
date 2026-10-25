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
using System.Text.Json.Serialization.Metadata;
using UiharuMind.Core.AI.Character.CharacterCards;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.Extensions;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core;

public static class SaveUtility
{
    /// <summary>
    /// 存档的序列化配置：缩进、宽容读端、未知类型读成 JsonNode、中文不转义。
    /// 类型只来自 <see cref="SaveJsonContext"/>，没有反射兜底——新存档类型要在那里登记
    /// </summary>
    public static readonly JsonSerializerOptions SaveJsonOptions = CreateSourceGenOptions();

    private static int _suppressSaveDepth;

    /// <summary>
    /// 反序列化期间是否应抑制 <see cref="Configs.ConfigBase.Save"/>。
    /// 配置类的属性 setter 里带落盘（如 <c>ThemeMode</c>），STJ 按 JSON 键顺序逐个调 setter，
    /// 每调一次就把<b>半成品</b>对象写盘——排在带 Save 的 setter 之后的普通属性（
    /// <c>FavoriteCharacterIds</c>、<c>SettingsScrollPositions</c> 等）还没被赋值，就被写成默认值，
    /// 把磁盘上已存在的值覆盖成空。加载期间抑制 Save，整份读完后再放行。
    /// </summary>
    internal static bool SaveSuppressed => Volatile.Read(ref _suppressSaveDepth) > 0;

    private static void EnterSuppressSave() => Interlocked.Increment(ref _suppressSaveDepth);

    private static void ExitSuppressSave() => Interlocked.Decrement(ref _suppressSaveDepth);

    private static JsonSerializerOptions CreateSourceGenOptions()
    {
        JsonSerializerOptions options = new(SaveJsonContext.Default.Options)
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true,
            UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        // 单点 converter：只挂 CharacterCard.Data 的两个 List<object> 宽容字段，不全局动 object
        options.TypeInfoResolver = options.TypeInfoResolver!.WithAddedModifier(ApplyObjectListConverters);
        options.MakeReadOnly();
        return options;
    }

    /// <summary>给 <see cref="Data.AlternateGreetings"/> / <see cref="Data.GroupOnlyGreetings"/> 挂单点 converter</summary>
    private static void ApplyObjectListConverters(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(Data)) return;
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (property.Name is "alternate_greetings" or "group_only_greetings")
            {
                property.CustomConverter = new SaveObjectListJsonConverter();
            }
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
            const int maxAttempts = 10; //Windows 上 8 并发替换同一目标的竞争窗口常超 60ms,3 次不够(实测偶发 Access denied 上报)
            for (int attempt = 1;; attempt++)
            {
                try
                {
                    File.WriteAllText(tempPath, content);
                    File.Move(tempPath, filePath, true);
                    return;
                }
                catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < maxAttempts)
                {
                    // 同一目标的并发替换等瞬态失败:退避重试。
                    // 内存里的对象才是权威,下一次保存还会重写,这里只为吞掉这次抖动。
                    // 注意 FileNotFoundException 也是 IOException,正好覆盖"目标被并发替换删掉"那一下。
                    // Windows 上覆盖 MoveFileEx 对正被并发替换的目标会报 Access denied(UnauthorizedAccess),
                    // 与 IOException 同为瞬态,一并重试
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
    /// 原子替换与并发语义同 <see cref="Save(string, object, JsonSerializerOptions?)"/>。
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
                const int maxAttempts = 10; //与 WriteAtomic 同口径:并发替换的竞争窗口要留足
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
                    catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < maxAttempts)
                    {
                        // 同 WriteAtomic:Windows 并发替换时的 Access denied 也是瞬态,一并重试
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
    /// 原子保存一个对象，按运行时类型序列化（设置一族经基类保存，派生字段也要写全）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="target">对象</param>
    /// <param name="options">序列化配置；省略为 <see cref="SaveJsonOptions"/>。会话存档要传自己那份（多态 AIContent）</param>
    public static void Save(string filePath, object target, JsonSerializerOptions? options = null)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (dir == null) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            WriteAtomic(filePath, JsonSerializer.Serialize(target, (options ?? SaveJsonOptions).GetTypeInfo(target.GetType())));
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
    /// <param name="options">序列化配置；省略为 <see cref="SaveJsonOptions"/></param>
    /// <typeparam name="T">目标类型</typeparam>
    /// <returns>对象；文件缺失或解析失败为 null</returns>
    public static T? Load<T>(string filePath, JsonSerializerOptions? options = null) where T : class
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            EnterSuppressSave();
            return JsonSerializer.Deserialize(File.ReadAllText(filePath), (options ?? SaveJsonOptions).GetTypeInfo<T>());
        }
        catch (Exception e)
        {
            Log.Warning($"Load File Error:{e.Message},Path:{filePath}");
            return null;
        }
        finally
        {
            ExitSuppressSave();
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

    /// <summary>
    /// 序列化成字符串
    /// </summary>
    /// <param name="target">对象</param>
    /// <param name="options">序列化配置；省略为 <see cref="SaveJsonOptions"/></param>
    /// <typeparam name="T">按这个类型写</typeparam>
    /// <returns>JSON 文本</returns>
    public static string SaveToString<T>(T target, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Serialize(target, (options ?? SaveJsonOptions).GetTypeInfo<T>());
    }

    //=========================Load=================================

    /// <summary>
    /// 从字符串读取，解析失败返回 <c>new T()</c>
    /// </summary>
    /// <param name="jsonString">JSON 文本</param>
    /// <param name="options">序列化配置；省略为 <see cref="SaveJsonOptions"/></param>
    /// <typeparam name="T">目标类型</typeparam>
    /// <returns>对象</returns>
    public static T LoadFromString<T>(string jsonString, JsonSerializerOptions? options = null) where T : new()
    {
        try
        {
            EnterSuppressSave();
            return JsonSerializer.Deserialize(jsonString, (options ?? SaveJsonOptions).GetTypeInfo<T>()) ?? new T();
        }
        catch (Exception e)
        {
            Log.Warning(e.ToString());
        }
        finally
        {
            ExitSuppressSave();
        }

        return new T();
    }

    /// <summary>
    /// 按运行时类型从字符串读取（远程模型配置修复用）
    /// </summary>
    /// <param name="jsonString">JSON 文本</param>
    /// <param name="type">目标类型，须在序列化配置里登记</param>
    /// <param name="options">序列化配置；省略为 <see cref="SaveJsonOptions"/></param>
    /// <returns>对象；失败为 null</returns>
    public static object? LoadFromString(string jsonString, Type? type, JsonSerializerOptions? options = null)
    {
        if (type == null) return null;
        try
        {
            EnterSuppressSave();
            return JsonSerializer.Deserialize(jsonString, (options ?? SaveJsonOptions).GetTypeInfo(type));
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
        finally
        {
            ExitSuppressSave();
        }

        return null;
    }
}
