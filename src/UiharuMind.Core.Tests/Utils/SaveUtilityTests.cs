using System.Text.Json;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 原子写盘的并发安全:同一文件的并发保存曾经共用 <c>filePath + ".tmp"</c>,
/// 先替换的吃掉 tmp,后到的以 FileNotFound 炸掉，打出 Save File Error 并弹出错误框。
/// </summary>
public class SaveUtilityTests
{
    private static string NewTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"save-atomic-{Guid.NewGuid():N}.jsonl");
    }

    /// <summary>多线程并发写同一文件:不报错、落盘的永远是某一次的完整快照、不留残留 tmp</summary>
    [Fact]
    public void ConcurrentSaveText_SameFile_NoErrorAndNoTornWrite()
    {
        string path = NewTempPath();
        string dir = Path.GetDirectoryName(path)!;
        List<LogIndexEntry> errors = [];
        void Handler(LogIndexEntry entry)
        {
            // 只认这个文件的:日志是全局的,并行跑的别的测试(如 TurnDriver 模拟断连)记的错也会流到这里
            if (entry.LogType == ELogType.Error && entry.Preview.Contains(Path.GetFileName(path)))
            {
                lock (errors) errors.Add(entry);
            }
        }

        LogManager.Instance.OnLogAppended += Handler;
        try
        {
            string[] payloads = Enumerable.Range(0, 16)
                .Select(i => new string((char)('A' + i), 4096))
                .ToArray();

            Parallel.ForEach(payloads, new ParallelOptions { MaxDegreeOfParallelism = 8 },
                payload => SaveUtility.SaveText(path, payload));

            LogManager.Instance.Flush();

            string final = File.ReadAllText(path);
            Assert.Contains(final, payloads);
            lock (errors) Assert.Empty(errors);
            Assert.Empty(Directory.GetFiles(dir, Path.GetFileName(path) + ".*.tmp"));
        }
        finally
        {
            LogManager.Instance.OnLogAppended -= Handler;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 测试残留清理失败不影响断言
            }
        }
    }

    /// <summary>流式写盘对非 ASCII 保持原样：writer 的 Encoder 继承 options，中文不退化成语 \uXXXX 转义</summary>
    [Fact]
    public void SaveUtf8_KeepsNonAsciiAsIs()
    {
        string path = NewTempPath();
        try
        {
            SaveUtility.SaveUtf8(path, writer =>
            {
                JsonSerializer.Serialize(writer, new { title = "世界观·白露" }, SessionJsonOptions.Index);
            }, SessionJsonOptions.Index);

            string text = File.ReadAllText(path);
            Assert.Contains("世界观·白露", text);
            Assert.DoesNotContain("\\u", text);
        }
        finally
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 测试残留清理失败不影响断言
            }
        }
    }

    /// <summary>流式写盘（P1）：不经过中间字符串也能写出完整 JSON，不缩进、不留 tmp</summary>
    [Fact]
    public void SaveUtf8_WritesCompactJson_ReadableBack_NoTempLeft()
    {
        string path = NewTempPath();
        try
        {
            SaveUtility.SaveUtf8(path, writer =>
            {
                writer.WriteStartArray();
                foreach (int n in new[] { 3, 1, 2 }) JsonSerializer.Serialize(writer, n, SessionJsonOptions.Index);
                writer.WriteEndArray();
            }, SessionJsonOptions.Index);

            string text = File.ReadAllText(path);
            Assert.DoesNotContain("\n", text.Trim());
            Assert.Equal(new[] { 3, 1, 2 }, JsonSerializer.Deserialize<List<int>>(text));

            string dir = Path.GetDirectoryName(path)!;
            Assert.Empty(Directory.GetFiles(dir, Path.GetFileName(path) + ".*.tmp"));
        }
        finally
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 测试残留清理失败不影响断言
            }
        }
    }
}
