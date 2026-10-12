using System.Text;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉住文件工具唯一的切行口径：\n、\r\n 与单独的 \r 都是行终止符。
/// Read 的行号与 Edit 的定位、diff 行号在老式 Mac 换行的文件上也要对得上
/// </summary>
public sealed class TextLinesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-textlines-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言
        }
    }

    [Theory]
    [InlineData("a\nb\nc", 3)]
    [InlineData("a\r\nb\r\nc\r\n", 3)]
    [InlineData("a\rb\rc", 3)]
    [InlineData("a\r\n\rb", 3)]
    [InlineData("", 0)]
    public void Split_TreatsAllThreeTerminatorsAlike(string text, int count)
    {
        List<TextLine> lines = TextLines.Split(text);

        Assert.Equal(count, lines.Count);
        Assert.Equal(TextLines.Enumerate(text), ReadLinesLikeStreamReader(text));
    }

    [Fact]
    public void Split_KeepsPositionsForEdit()
    {
        List<TextLine> lines = TextLines.Split("ab\r\ncd\ref");

        Assert.Equal([new TextLine(0, 2, 4), new TextLine(4, 6, 7), new TextLine(7, 9, 9)], lines);
        Assert.Equal(1, TextLines.IndexOf(lines, 5));
    }

    /// <summary>老式 Mac 换行的文件：Read 的行号就是 Edit diff 的行号，新插入的行沿用 \r，不混进 \n</summary>
    [Fact]
    public async Task ReadAndEdit_AgreeOnLineNumbers_ForCarriageReturnOnlyFiles()
    {
        string path = Path.Combine(_dir, "mac.txt");
        await File.WriteAllTextAsync(path, "one\rtwo\rthree\r", TestContext.Current.CancellationToken);
        var tools = new PermissiveFileAccessTools(_dir);
        CancellationToken ct = TestContext.Current.CancellationToken;

        string read = await tools.Read(path, 2, 1, cancellationToken: ct);
        FileEditPlan plan = FileEditPlanner.Plan(await File.ReadAllTextAsync(path, ct), "mac.txt",
            [new FileEdit { OldString = "two\nthree", NewString = "two\nnew\nthree" }]);

        Assert.StartsWith("two\n", read);
        Assert.True(plan.Succeeded, plan.Error);
        Assert.Equal("one\rtwo\rnew\rthree\r", plan.NewText);
        Assert.Contains(plan.Diff, entry => entry.Text == "new" && entry.LineNumber == 3);
    }

    private static List<string> ReadLinesLikeStreamReader(string text)
    {
        using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        List<string> lines = [];
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines;
    }
}
