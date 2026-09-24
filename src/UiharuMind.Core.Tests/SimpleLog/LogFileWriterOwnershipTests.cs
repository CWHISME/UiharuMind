using System.Text;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Tests.SimpleLog;

/// <summary>
/// 多实例共用同一日志目录时的所有权防线。
/// 钉住的事实：后启动的实例 <c>RotateOnStartup</c> 会把先启动实例的 <c>Log.txt</c>
/// 改名并新开一个；先启动实例的索引必须经过校准仍能读回自己的旧条目，
/// 而不是 read=0 变成空白/空串。
/// </summary>
public class LogFileWriterOwnershipTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-logown-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
            // ignore
        }
    }

    /// <summary>
    /// 第二个实例启动后，第一个实例读自己的旧条目仍应拿回原文——
    /// 校准把 currentFileId 对齐到「我的数据被推到的世代」，ResolvePath 指向改名后的文件
    /// </summary>
    [Fact]
    public void SecondInstanceStartup_OldInstanceStillReadsOwnEntries()
    {
        // 预置一份 Log.txt，让第一个实例启动时触发 RotateOnStartup（currentFileId 0→1）
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "Log.txt"), "旧文件占位\n");

        using LogStore first = new(_directory);
        LogIndexEntry firstEntry = default!;
        for (int i = 0; i < 2000; i++)
        {
            firstEntry = first.Append(new LogItem(ELogType.Log,
                $"第一实例第 {i} 条正文 " + new string('x', 500)));
        }
        first.Flush();
        string expected = "第一实例第 1999 条正文 " + new string('x', 500);
        Assert.Equal(expected, first.ReadText(firstEntry)); // 单实例时能读回

        // 第二个实例启动：RotateOnStartup 把 first 的 Log.txt 改名 Log.1，新开 Log.txt
        using LogStore second = new(_directory);
        second.Append(new LogItem(ELogType.Log, "第二实例正文"));
        second.Flush();

        // first 的旧索引经校准后应重新指向改名后的文件，正文一字不差地读得回
        Assert.Equal(expected, first.ReadText(firstEntry));
    }

    /// <summary>
    /// 当前文件被外部换成一个更短的文件（别的实例新开/截断）时，
    /// 读回必须是 null（死链）而不是空串——空串正是「详情面板空白」的直接来源
    /// </summary>
    [Fact]
    public void Read_FileReplacedShorter_ReturnsNullNotBlank()
    {
        using LogFileWriter writer = new(_directory, "T", maxBytes: 1024 * 1024, generations: 3);
        byte[] payload = Encoding.UTF8.GetBytes("0123456789abcdefghij");
        long offset = writer.Append(payload);
        int fileId = writer.CurrentFileId;
        writer.Flush();
        Assert.Equal("0123456789abcdefghij", writer.Read(fileId, offset, payload.Length));

        // 模拟别的实例把 T.txt 换成一个短文件（旧正文随之丢失/被改名）
        File.WriteAllText(Path.Combine(_directory, "T.txt"), "新的短内容");

        Assert.Null(writer.Read(fileId, offset, payload.Length));
    }
}
