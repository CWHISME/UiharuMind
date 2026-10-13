using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// Edit 部分写入的回执（ADR 0087）：结论在最前，没生效的条目紧随其后，并明说只重发它们；
/// 仍按落盘写入计（群产物区认这个开头）
/// </summary>
public class EditPartialWriteTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-partial-").FullName;

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

    [Fact]
    public async Task PartialEdit_WritesTheMatchedOnes_AndNamesTheRest()
    {
        string path = Path.Combine(_dir, "a.txt");
        await File.WriteAllTextAsync(path, "one\ntwo\n", TestContext.Current.CancellationToken);
        var tools = new PermissiveFileAccessTools(_dir);

        string result = await tools.Edit("a.txt",
            [new FileEdit { OldString = "one", NewString = "ONE" }, new FileEdit { OldString = "nowhere", NewString = "x" }],
            TestContext.Current.CancellationToken);

        Assert.Equal("ONE\ntwo\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.StartsWith("已修改 'a.txt'（部分成功：1/2 处编辑）。", result);
        Assert.Contains("edits[1]", result);
        Assert.Contains("已生效的不要重发", result);
        Assert.True(FileToolNames.IsSuccessfulWrite(result));
    }
}
