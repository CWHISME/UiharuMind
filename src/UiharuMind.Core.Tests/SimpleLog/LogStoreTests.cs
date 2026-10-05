using System.Text;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Tests.SimpleLog;

/// <summary>
/// <see cref="LogStore"/> 的落盘与读回。钉住的事实：磁盘是唯一真相源、正文一字不截、
/// 大正文外置后主流仍然人类可读、偏移量按字节算（中文不能错位）。
/// <para>
/// 拿的是临时目录里的独立实例，不碰生产单例——这也是把存储引擎从
/// <c>LogManager</c> 里拆出来的收益之一。
/// </para>
/// </summary>
public class LogStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-log-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
            // 清理失败不该让测试变红
        }
    }

    /// <summary>
    /// 短正文内联在主流里，读回来必须与写进去的一模一样。
    /// 用中文钉住「偏移量按字节而不是字符算」——按字符算会在这里直接错位
    /// </summary>
    [Fact]
    public void Append_InlineText_ReadsBackExactly()
    {
        using LogStore store = new(_directory);
        const string text = "中文正文\n第二行 with ascii\n第三行";

        LogIndexEntry entry = store.Append(new LogItem(ELogType.Log, text));

        Assert.Equal(ELogStream.Main, entry.Stream);
        Assert.Equal(text, store.ReadText(entry));
    }

    /// <summary>
    /// 超过阈值的正文外置到 Bodies.txt：正文一字不少地读得回来，
    /// 而主流里那条只剩摘要——这正是 Log.txt 能保持人类可读的原因
    /// </summary>
    [Fact]
    public void Append_BeyondSpillThreshold_KeepsMainReadable()
    {
        using LogStore store = new(_directory);
        string text = "首行摘要\n" + new string('x', LogFormat.SpillThreshold * 2);

        LogIndexEntry entry = store.Append(new LogItem(ELogType.Log, text, ELogCategory.LlmRequest));
        store.Flush();

        Assert.Equal(ELogStream.Bodies, entry.Stream);
        Assert.Equal(text, store.ReadText(entry));

        string main = File.ReadAllText(Path.Combine(_directory, "Log.txt"));
        Assert.Contains("-> Bodies.txt#", main);
        Assert.Contains("首行摘要", main);
        Assert.True(main.Length < 500, $"主流被大正文撑到了 {main.Length} 字符");
    }

    /// <summary>
    /// 正文不截断。原实现有一道 256KB 的兜底闸，
    /// 恰恰把最需要看清的大上下文请求参数削掉了
    /// </summary>
    [Fact]
    public void Append_HugeText_IsNotTruncated()
    {
        using LogStore store = new(_directory);
        string text = new('y', 2 * 1024 * 1024);

        LogIndexEntry entry = store.Append(new LogItem(ELogType.Log, text));

        Assert.Equal(text.Length, store.ReadText(entry)?.Length);
    }

    /// <summary>
    /// 索引项只带预览，且预览只取首行。后面还有内容时补省略号——
    /// 不补的话，正好截在可视宽度上的那条看着像是完整的
    /// </summary>
    [Fact]
    public void Append_Preview_TakesFirstLineWithEllipsis()
    {
        using LogStore store = new(_directory);

        Assert.Equal("第一行…", store.Append(new LogItem(ELogType.Warning, "第一行\n第二行")).Preview);
        Assert.Equal("只有一行", store.Append(new LogItem(ELogType.Warning, "只有一行")).Preview);
        Assert.Equal(LogFormat.PreviewLength + 1,
            store.Append(new LogItem(ELogType.Log, new string('x', LogFormat.PreviewLength * 2))).Preview.Length);
    }

    /// <summary>
    /// 字节正文与字符串正文落成同一种形态：读回的是「引导文字 + 正文」，偏移量按字节算，
    /// 头行报的是字符数（与字符串正文同口径）
    /// </summary>
    [Fact]
    public void Append_Utf8Body_ReadsBackLeadAndBody()
    {
        using LogStore store = new(_directory);
        const string body = "{\n  \"content\": \"中文正文\"\n}";

        LogIndexEntry first = store.Append(Utf8Item("请求 (1 条): ", body));
        LogIndexEntry second = store.Append(new LogItem(ELogType.Log, "之后的一条"));
        store.Flush();

        Assert.Equal(ELogStream.Main, first.Stream);
        Assert.Equal("请求 (1 条): " + body, store.ReadText(first));
        Assert.Equal("之后的一条", store.ReadText(second));
        Assert.Equal("请求 (1 条): {…", first.Preview);
        Assert.Contains($"({("请求 (1 条): " + body).Length:N0} chars)", File.ReadAllText(Path.Combine(_directory, "Log.txt")));
    }

    /// <summary>外置与否按字符数判（与字符串正文同口径）：中文按字节早就超了阈值，按字符没超就仍在主流</summary>
    [Fact]
    public void Append_Utf8Body_SpillsByCharCount()
    {
        using LogStore store = new(_directory);
        string inline = new('中', LogFormat.SpillThreshold - 10);
        string spilled = "首行\n" + new string('中', LogFormat.SpillThreshold);

        LogIndexEntry kept = store.Append(Utf8Item("", inline));
        LogIndexEntry moved = store.Append(Utf8Item("lead: ", spilled));

        Assert.Equal(ELogStream.Main, kept.Stream);
        Assert.Equal(inline, store.ReadText(kept));
        Assert.Equal(ELogStream.Bodies, moved.Stream);
        Assert.Equal("lead: " + spilled, store.ReadText(moved));
        Assert.Equal("lead: 首行…", moved.Preview);
    }

    /// <summary>预览只解码首行用得上的那段：超长首行照样截到上限并补省略号，引导文字里有换行就只取它的首行</summary>
    [Fact]
    public void Utf8Preview_TruncatesLikeTextPreview()
    {
        string longLine = new('长', LogFormat.PreviewLength * 3);

        Assert.Equal(LogFormat.Preview("头" + longLine), LogFormat.Preview("头", Encoding.UTF8.GetBytes(longLine)));
        Assert.Equal("只有一行", LogFormat.Preview("只有", "一行"u8));
        Assert.Equal("失败: 429…", LogFormat.Preview("失败: 429\n", "{}"u8));
        Assert.Equal("空正文", LogFormat.Preview("空正文", ReadOnlySpan<byte>.Empty));
    }

    private static LogItem Utf8Item(string lead, string body) =>
        new(ELogType.Log, lead, Encoding.UTF8.GetBytes(body), ELogCategory.LlmRequest);

    /// <summary>
    /// 旧版退出时写的是 JSON 数组，与新格式不兼容。启动时识别到就删，不写迁移
    /// </summary>
    [Fact]
    public void Ctor_LegacyJsonFiles_AreDeleted()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "Log.txt"), "[\n  {\n    \"LogString\": \"old\"\n  }\n]");
        File.WriteAllText(Path.Combine(_directory, "LastLog.txt"), "[]");

        using LogStore store = new(_directory);

        Assert.False(File.Exists(Path.Combine(_directory, "LastLog.txt")));
        Assert.False(File.Exists(Path.Combine(_directory, "Log.1.txt"))); //旧文件是被删掉,不是被滚动成第 1 代
    }

    /// <summary>
    /// 上一次运行的日志在启动时滚成第 1 代，本次运行从干净的文件开始
    /// </summary>
    [Fact]
    public void Ctor_PreviousRun_IsRotatedNotOverwritten()
    {
        using (LogStore first = new(_directory))
        {
            first.Append(new LogItem(ELogType.Log, "上一次运行"));
        }

        using LogStore second = new(_directory);
        second.Append(new LogItem(ELogType.Log, "这一次运行"));
        second.Flush();

        Assert.Contains("上一次运行", File.ReadAllText(Path.Combine(_directory, "Log.1.txt")));
        Assert.Contains("这一次运行", File.ReadAllText(Path.Combine(_directory, "Log.txt")));
    }

    /// <summary>快照是副本，调用方改动它不影响内部索引</summary>
    [Fact]
    public void GetSnapshot_ReturnsIndependentCopy()
    {
        using LogStore store = new(_directory);
        store.Append(new LogItem(ELogType.Log, "a"));

        List<LogIndexEntry> first = store.GetSnapshot();
        first.Clear();

        Assert.Single(store.GetSnapshot());
    }
}

/// <summary>
/// <see cref="LogFileWriter"/> 的滚动。钉住的事实：滚动只是改名，
/// <b>已记下的偏移量依然有效</b>；滚出保留代数之后才变成死链。
/// </summary>
public class LogFileWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-logw-{Guid.NewGuid():N}");

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

    /// <summary>滚动一代之后，旧偏移量仍然读得回原来那段内容</summary>
    [Fact]
    public void Read_AfterRotation_OldOffsetStillValid()
    {
        using LogFileWriter writer = new(_directory, "T", maxBytes: 32, generations: 3);
        byte[] first = Encoding.UTF8.GetBytes("0123456789abcdefghij");

        long offset = writer.Append(first);
        int fileId = writer.CurrentFileId;
        writer.Append(Encoding.UTF8.GetBytes("0123456789abcdefghij")); //撑过上限,触发滚动

        Assert.NotEqual(fileId, writer.CurrentFileId);
        Assert.Equal("0123456789abcdefghij", writer.Read(fileId, offset, first.Length));
    }

    /// <summary>滚出保留代数之后是死链，读回 null 而不是错位的内容</summary>
    [Fact]
    public void Read_AfterRotatedOutOfRetention_ReturnsNull()
    {
        using LogFileWriter writer = new(_directory, "T", maxBytes: 8, generations: 2);
        byte[] payload = Encoding.UTF8.GetBytes("0123456789");

        long offset = writer.Append(payload);
        int fileId = writer.CurrentFileId;
        for (int i = 0; i < 3; i++) writer.Append(payload);

        Assert.Null(writer.Read(fileId, offset, payload.Length));
    }
}

/// <summary>
/// <see cref="LogManager"/> 的异步写入链路。钉住的事实：业务线程只入队，
/// 落盘与派发都在后台单写线程上，且订阅方抛异常不会把写入线程或业务线程带走。
/// </summary>
public class LogManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-logm-{Guid.NewGuid():N}");

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

    /// <summary>打进去的日志最终会落盘、进索引，并把索引项派发给订阅方</summary>
    [Fact]
    public void Log_IsAppendedAndDispatched()
    {
        LogManager manager = new(_directory);
        LogIndexEntry? dispatched = null;
        manager.OnLogAppended += entry => dispatched = entry;

        manager.Log("hello 日志");
        manager.Flush();

        Assert.Single(manager.GetSnapshot());
        Assert.Equal("hello 日志", manager.ReadText(manager.GetSnapshot()[0]));
        manager.Shutdown();
        Assert.NotNull(dispatched);
    }

    /// <summary>字节正文入队前就拷了一份：调用方返回后立刻改写自己的缓冲，落盘的仍是原内容</summary>
    [Fact]
    public void LogUtf8_CopiesTheBodyBeforeReturning()
    {
        LogManager manager = new(_directory);
        byte[] body = Encoding.UTF8.GetBytes("正文 body");

        manager.Log("lead ", body, ELogCategory.LlmRequest);
        body.AsSpan().Fill((byte)'x');
        manager.Flush();

        LogIndexEntry entry = Assert.Single(manager.GetSnapshot());
        Assert.Equal("lead 正文 body", manager.ReadText(entry));
        Assert.Equal(ELogCategory.LlmRequest, entry.Category);
        manager.Shutdown();
    }

    /// <summary>
    /// 订阅方抛异常，既不能炸业务线程，也不能让后台写入线程死掉——
    /// 死了的话之后所有日志都会静默丢失
    /// </summary>
    [Fact]
    public void Log_SubscriberThrows_WriterKeepsRunning()
    {
        LogManager manager = new(_directory);
        manager.OnLogAppended += _ => throw new InvalidOperationException("boom");

        manager.Log("before"); //不抛
        manager.Log("after");
        manager.Flush();

        Assert.Equal(2, manager.GetSnapshot().Count);
        manager.Shutdown();
    }
}
