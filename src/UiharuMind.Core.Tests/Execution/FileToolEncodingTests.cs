using System.Text;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 文件工具对非 UTF-8 文件的读、搜、改、写，与知识库同一份编码探测。
/// 最该钉住的是从前那件静默毁文件的事：GBK 文件经 Edit 只改一行 ASCII，全文中文就成了「锟斤拷」
/// </summary>
public sealed class FileToolEncodingTests : IDisposable
{
    private const string Code = "using System;\nnamespace A;\npublic class Foo\n{\n    public int Bar() => 42;\n}\n";
    private const string Japanese = "// 日本語のコメントです。ファイルを読み込みます。\n" + Code;

    private static readonly Encoding Gb18030;
    private static readonly Encoding ShiftJis;

    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-encoding-").FullName;
    private readonly PermissiveFileAccessTools _tools;

    static FileToolEncodingTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Gb18030 = Encoding.GetEncoding("gb18030");
        ShiftJis = Encoding.GetEncoding("shift_jis");
    }

    public FileToolEncodingTests()
    {
        _tools = new PermissiveFileAccessTools(_dir, new FileBackupStore(Path.Combine(_dir, ".backups")));
    }

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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Save(string name, byte[] bytes)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private Task<string> Edit(string path, string oldString, string newString) =>
        _tools.Edit(path, [new FileEdit { OldString = oldString, NewString = newString }], Ct);

    [Fact]
    public async Task Read_GbkFile_ShowsChinese_AndNamesTheEncoding()
    {
        string path = Save("a.cs", Gb18030.GetBytes("// 返回默认值\nint a = 1;\n"));

        string result = await _tools.Read(path, cancellationToken: Ct);

        Assert.Equal("// 返回默认值\nint a = 1;\n[编码：gb18030]", result);
    }

    [Fact]
    public async Task Read_Utf16File_ShowsTheText()
    {
        string path = Save("a.txt", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("第一行\nline two\n")]);

        string result = await _tools.Read(path, cancellationToken: Ct);

        Assert.Equal("第一行\nline two\n[编码：utf-16]", result);
    }

    [Fact]
    public async Task Edit_GbkFile_KeepsTheEncoding_AndEveryUntouchedByte()
    {
        string path = Save("a.cs", Gb18030.GetBytes("// 返回默认值\nint a = 1;\n// 结尾注释\n"));

        string result = await Edit(path, "int a = 1;", "int a = 2; // 改过");

        Assert.StartsWith(FileToolNames.EditSucceededPrefix, result);
        Assert.Equal(Gb18030.GetBytes("// 返回默认值\nint a = 2; // 改过\n// 结尾注释\n"),
            await File.ReadAllBytesAsync(path, Ct));
    }

    /// <summary>坏字节解码时已成了替换字符：写回去会把没碰到的那处也改掉，所以整个拒绝</summary>
    [Fact]
    public async Task Edit_FileWithBytesItsEncodingCannotHold_IsRefused_AndLeftUntouched()
    {
        byte[] original = [.. Encoding.UTF8.GetBytes("// 这里是一段正常的中文注释\n"), 0xFF, .. "int a = 1;\n"u8];
        string path = Save("a.cs", original);

        string result = await Edit(path, "int a = 1;", "int a = 2;");

        Assert.Contains("无法安全编辑", result);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, Ct));
    }

    /// <summary>改一小段不该把整个文件换成别的编码：写不下就报错，不悄悄写成「?」</summary>
    [Fact]
    public async Task Edit_CharactersTheEncodingCannotStore_AreRefused()
    {
        byte[] original = ShiftJis.GetBytes(Japanese);
        string path = Save("a.cs", original);

        string result = await Edit(path, "=> 42;", "=> 42; // 😀");

        Assert.Contains("shift_jis 存不下", result);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, Ct));
    }

    [Fact]
    public async Task Write_GbkFile_StaysGbk()
    {
        string path = Save("a.cs", Gb18030.GetBytes("// 旧内容\n"));

        await _tools.Write(path, "// 新内容\n", Ct);

        Assert.Equal(Gb18030.GetBytes("// 新内容\n"), await File.ReadAllBytesAsync(path, Ct));
    }

    /// <summary>整份替换的意图是明确的：原编码写不下就改存 UTF-8，并在结果里说出来</summary>
    [Fact]
    public async Task Write_ContentTheEncodingCannotStore_IsSavedAsUtf8_AndSaysSo()
    {
        string path = Save("a.cs", ShiftJis.GetBytes(Japanese));

        string result = await _tools.Write(path, "// 😀\n", Ct);

        Assert.Contains("已改存为 UTF-8", result);
        Assert.Equal(Encoding.UTF8.GetBytes("// 😀\n"), await File.ReadAllBytesAsync(path, Ct));
    }

    [Fact]
    public async Task Grep_GbkFile_FindsChinese_AndShowsTheLineReadably()
    {
        Save("a.cs", Gb18030.GetBytes("// 返回默认值\nint a = 1;\n"));

        string result = await _tools.Grep("默认值", ct: Ct);

        Assert.Contains("// 返回默认值", result);
    }

    /// <summary>UTF-16 的 NUL 从前让整个文件被当成二进制跳过</summary>
    [Fact]
    public async Task Grep_Utf16File_IsSearched()
    {
        Save("a.txt", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("first\nthe needle here\n")]);

        string result = await _tools.Grep("needle", ct: Ct);

        Assert.Contains("the needle here", result);
    }
}
