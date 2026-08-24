using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死数组参数的宽容读法。
///
/// 实机症状：<c>Grep(fileGlobs: "*.cpp")</c> 直接死在反序列化上，模型拿回
/// <c>The JSON value could not be converted to System.String[]</c>——一句它既看不懂
/// 也无从改正的框架异常，连哪个参数错了都没说。
/// 「schema 说要数组、模型给了标量」是模型的通病，所以治在序列化层，不是改这一个参数的类型。
/// </summary>
public class ToolArgumentLenienceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-lenient-").FullName;
    private readonly PermissiveFileAccessTools _tools;

    public ToolArgumentLenienceTests()
    {
        _tools = new PermissiveFileAccessTools(_dir);
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

    /// <summary>
    /// <b>走真实的工具调用路径</b>，不是只测转换器：转换器接对了但没挂到工具上，
    /// 实机照样会炸，而单测转换器发现不了那种漏接。
    /// </summary>
    [Fact]
    public async Task Grep_AcceptsScalarStringForFileGlobs()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "a.cpp"), "wayland");
        await File.WriteAllTextAsync(Path.Combine(_dir, "b.txt"), "wayland");

        AIFunction grep = _tools.Create().OfType<AIFunction>()
            .Single(x => x.Name == FileToolNames.Grep);

        // 模型实际发出来的那一种:fileGlobs 给了标量字符串而不是数组
        object? result = await grep.InvokeAsync(new AIFunctionArguments
        {
            ["query"] = "wayland",
            ["isRegex"] = false,
            ["fileGlobs"] = "*.cpp",
        });

        string json = JsonSerializer.Serialize(result);
        Assert.Contains("a.cpp", json);
        Assert.DoesNotContain("b.txt", json); //过滤真生效了,不是把它当没传
    }

    [Fact]
    public void ScalarString_BecomesSingleElement()
    {
        Assert.Equal(["*.cpp"], Required("\"*.cpp\""));
    }

    /// <summary>一个标量里塞多项时按逗号拆开——模型也会这么写</summary>
    [Fact]
    public void CommaSeparatedScalar_IsSplit()
    {
        Assert.Equal(["*.cpp", "*.h"], Required("\"*.cpp,*.h\""));
    }

    /// <summary>
    /// 带花括号的<b>一律不拆</b>：glob 的分组语法本身就用逗号，
    /// 拆了会把一个写对了的表达式毁掉。
    /// </summary>
    [Fact]
    public void BraceGlob_IsNeverSplit()
    {
        Assert.Equal(["*.{cpp,h}"], Required("\"*.{cpp,h}\""));
    }

    [Fact]
    public void ProperArray_StillWorks()
    {
        Assert.Equal(["*.cpp", "*.h"], Required("[\"*.cpp\",\"*.h\"]"));
    }

    [Fact]
    public void EmptyStringAndNull_AreHandled()
    {
        Assert.Empty(Required("\"\""));
        Assert.Null(Deserialize("null"));
    }

    private static string[]? Deserialize(string json) =>
        JsonSerializer.Deserialize<string[]>(json, ToolJson.Lenient);

    private static string[] Required(string json) => Assert.IsType<string[]>(Deserialize(json));
}
