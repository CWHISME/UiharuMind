using System.Text.Json;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死 Edit 工具 edits 参数的宽容读法。模型实机踩过的三种怪癖：
/// 整个 edits 传成 JSON 字符串、漏数组直接传单对象、属性名写成 underscore。
/// 对应 pi 的 prepareArguments 兼容层处理的同一类问题。
/// </summary>
public class LenientFileEditListConverterTests
{
    private static List<FileEdit>? Deserialize(string json)
        => JsonSerializer.Deserialize<List<FileEdit>>(json, ToolJson.Lenient);

    [Fact]
    public void NormalArray_ReadsAsIs()
    {
        List<FileEdit> edits = Deserialize(
                """[{"oldString":"a","newString":"b"},{"oldString":"c","newString":"d"}]""")
            ?? throw new InvalidOperationException("must not be null");

        Assert.Equal(2, edits.Count);
        Assert.Equal(("a", "b"), (edits[0].OldString, edits[0].NewString));
        Assert.Equal(("c", "d"), (edits[1].OldString, edits[1].NewString));
    }

    [Fact]
    public void UnderscorePropertyNames_AreAbsorbed()
    {
        List<FileEdit> edits = Deserialize(
                """[{"old_string":"a","new_string":"b"}]""")
            ?? throw new InvalidOperationException("must not be null");

        Assert.Single(edits);
        Assert.Equal(("a", "b"), (edits[0].OldString, edits[0].NewString));
    }

    [Fact]
    public void SingleObject_MissingArray_IsWrapped()
    {
        List<FileEdit> edits = Deserialize("""{"oldString":"a","newString":"b"}""")
            ?? throw new InvalidOperationException("must not be null");

        Assert.Single(edits);
        Assert.Equal(("a", "b"), (edits[0].OldString, edits[0].NewString));
    }

    [Fact]
    public void JsonString_ContainingArray_IsParsedTwice()
    {
        List<FileEdit> edits = Deserialize("\"[{\\\"oldString\\\":\\\"a\\\",\\\"newString\\\":\\\"b\\\"}]\"")
            ?? throw new InvalidOperationException("must not be null");

        Assert.Single(edits);
        Assert.Equal(("a", "b"), (edits[0].OldString, edits[0].NewString));
    }

    [Fact]
    public void JsonString_ContainingSingleObject_IsParsedTwice()
    {
        List<FileEdit> edits = Deserialize("\"{\\\"oldString\\\":\\\"a\\\",\\\"newString\\\":\\\"b\\\"}\"")
            ?? throw new InvalidOperationException("must not be null");

        Assert.Single(edits);
        Assert.Equal(("a", "b"), (edits[0].OldString, edits[0].NewString));
    }

    [Fact]
    public void Null_IsPassedThrough()
    {
        Assert.Null(Deserialize("null"));
    }

    [Fact]
    public void EmptyString_IsEmptyEditList()
    {
        Assert.Empty(Deserialize("\"\"")!);
        Assert.Empty(Deserialize("\"   \"")!);
    }

    [Fact]
    public void NonJsonString_ThrowsActionableMessage()
    {
        // 模型把 edits 发成普通文本时,必须回一条能照着改的话术,而不是框架的裸 JsonException
        JsonException ex = Assert.Throws<JsonException>(() => Deserialize("\"replace foo with bar\""));
        Assert.Contains("edits must be a JSON array of {oldString,newString} objects", ex.Message);
        Assert.Contains("replace foo with bar", ex.Message);
    }

    [Fact]
    public void ScalarRoot_ThrowsActionableMessage()
    {
        // 构造：edits 传成 JSON 字符串，字符串内容是一个合法的标量 JSON（"foo"）
        // ——用 Serialize 生成字面量，避免手写转义出错
        string scalarJson = JsonSerializer.Serialize("\"foo\""); // 值为 "foo" 的 JSON 字符串
        JsonException ex = Assert.Throws<JsonException>(() => Deserialize(scalarJson));
        Assert.Contains("edits must be an array of {oldString,newString} objects", ex.Message);
    }

    [Fact]
    public void ArrayWithNonObjectElement_ThrowsActionableMessage()
    {
        JsonException ex = Assert.Throws<JsonException>(() => Deserialize("[\"oops\",{\"oldString\":\"a\",\"newString\":\"b\"}]"));
        Assert.Contains("Each edit must be an object", ex.Message);
    }
}