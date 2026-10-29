using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 工具结果在<b>内存里</b>的形态必须和读盘之后一样。
///
/// 工厂带了 <c>SerializerOptions</c>（参数要宽松解析），框架会拿同一份选项把返回值序列化成
/// <c>JsonElement</c>：字符串结果于是变成「带引号与 <c>\t</c> 字面量的 JSON 文本」，发给模型时
/// 原样上线，模型读到的是转义串；而历史读盘时 <c>ChatContentNormalizer</c> 又会把它还原成 string。
/// 同一份历史两种字节，前缀从第一条 tool 消息起就分叉——实测一次这样的分叉让后面 32 条消息全部冷启动。
/// </summary>
public class ToolResultShapeTests
{
    [Fact]
    public async Task StringResult_StaysString()
    {
        AIFunction function = ToolJson.CreateFunction(
            (string path) => Task.FromResult("1\t# AGENTS.md\n2\t\n3\t内容"), "Probe");

        object? result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "x" }),
            TestContext.Current.CancellationToken);

        Assert.IsType<string>(result);
        Assert.Equal("1\t# AGENTS.md\n2\t\n3\t内容", result);
    }

    /// <summary>真工具走一遍：Read 的正文必须是原文，不是被序列化过的 JSON 文本</summary>
    [Fact]
    public async Task RealReadTool_ReturnsRawText()
    {
        string root = Path.Combine(Path.GetTempPath(), "uiharu-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.md"), "# 标题\n\n正文\n",
                TestContext.Current.CancellationToken);

            AITool read = new PermissiveFileAccessTools(root).Create().Single(t => t.Name == "Read");
            object? result = await ((AIFunction)read).InvokeAsync(
                new AIFunctionArguments(new Dictionary<string, object?> { ["filePath"] = "a.md" }),
                TestContext.Current.CancellationToken);

            string text = Assert.IsType<string>(result);
            Assert.Contains("# 标题", text);
            Assert.DoesNotContain("\\t", text); // 序列化过的形态里换行是 \n 字面量
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>结构化结果保持对象形态：那是工具特意给模型的结构，抹成文本会丢信息</summary>
    [Fact]
    public async Task StructuredResult_KeepsJsonShape()
    {
        AIFunction function = ToolJson.CreateFunction(
            (string path) => Task.FromResult(new Dictionary<string, object?> { ["a"] = 1 }), "ProbeStructured");

        object? result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "x" }),
            TestContext.Current.CancellationToken);

        Assert.IsType<System.Text.Json.JsonElement>(result);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, ((System.Text.Json.JsonElement)result!).ValueKind);
    }
}
