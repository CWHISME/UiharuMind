using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死文本工具调用恢复的语义:GLM 线格式的调用漏成纯文本时(尤其思考通道)
/// 必须转回结构化调用,否则框架循环判定"没有工具调用"而终止一轮;
/// 名字不命中已装配工具的块必须原文放行;跨增量切断安全;流尾不吞内容。
/// </summary>
public class TextToolCallRecoveryTests
{
    private static readonly HashSet<string> Tools = new(StringComparer.Ordinal) { "Glob", "Grep", "Read" };

    [Fact]
    public void RealWorldLeak_TwoCallsInReasoning_AreRecovered()
    {
        // 实录:GLM 思考模式把两次 Glob 调用写进 reasoning 通道,正文为空,一轮就此终止
        const string leaked = "让我继续探索这两个目录的内容喵～" +
                              "<tool_call>Glob<arg_key>pattern</arg_key><arg_value>LineTest/**/*</arg_value></tool_call>" +
                              "<tool_call>Glob<arg_key>pattern</arg_key><arg_value>URPShader/**/*</arg_value></tool_call>";

        TextToolCallStreamParser parser = new(Tools);
        (string text, List<FunctionCallContent> calls) = parser.Feed(leaked);
        (string tail, List<FunctionCallContent> tailCalls) = parser.Flush();

        Assert.Equal("让我继续探索这两个目录的内容喵～", text + tail);
        Assert.Empty(tailCalls);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Equal("Glob", c.Name));
        Assert.Equal("LineTest/**/*", calls[0].Arguments!["pattern"]);
        Assert.Equal("URPShader/**/*", calls[1].Arguments!["pattern"]);
        Assert.NotEqual(calls[0].CallId, calls[1].CallId);
    }

    [Fact]
    public void CallSplitAcrossDeltas_IsStillRecovered()
    {
        TextToolCallStreamParser parser = new(Tools);
        List<FunctionCallContent> calls = [];
        string text = "";
        foreach (string delta in new[]
                 {
                     "先看看<tool_ca", "ll>Grep<arg_key>que", "ry</arg_key><arg_value>needle</arg_value></tool_call>好了",
                 })
        {
            (string t, var c) = parser.Feed(delta);
            text += t;
            calls.AddRange(c);
        }

        (string tail, var tailCalls) = parser.Flush();
        text += tail;
        calls.AddRange(tailCalls);

        Assert.Equal("先看看好了", text);
        FunctionCallContent call = Assert.Single(calls);
        Assert.Equal("Grep", call.Name);
        Assert.Equal("needle", call.Arguments!["query"]);
    }

    [Fact]
    public void UnknownToolName_PassesThroughAsText()
    {
        const string input = "语法示例:<tool_call>NotATool<arg_key>x</arg_key><arg_value>1</arg_value></tool_call>";
        TextToolCallStreamParser parser = new(Tools);

        (string text, var calls) = parser.Feed(input);
        (string tail, var tailCalls) = parser.Flush();

        Assert.Empty(calls);
        Assert.Empty(tailCalls);
        Assert.Equal(input, text + tail); //讨论这种语法的正常文本原样保留
    }

    [Fact]
    public void UnclosedBlockAtStreamEnd_IsFlushedAsRawText()
    {
        TextToolCallStreamParser parser = new(Tools);
        (string text, var calls) = parser.Feed("残缺<tool_call>Glob<arg_key>pattern</arg_key>");
        (string tail, var tailCalls) = parser.Flush();

        Assert.Empty(calls);
        Assert.Empty(tailCalls);
        Assert.Equal("残缺<tool_call>Glob<arg_key>pattern</arg_key>", text + tail); //绝不吞内容
    }

    [Fact]
    public void PlainTextWithoutCalls_IsUntouched()
    {
        TextToolCallStreamParser parser = new(Tools);
        (string text, var calls) = parser.Feed("普通回答,提到 <b>标签</b> 也没事。");
        (string tail, _) = parser.Flush();

        Assert.Empty(calls);
        Assert.Equal("普通回答,提到 <b>标签</b> 也没事。", text + tail);
    }

    [Fact]
    public void MultipleArgPairs_AreAllParsed()
    {
        const string input = "<tool_call>Read<arg_key>filePath</arg_key><arg_value>a.cs</arg_value>" +
                             "<arg_key>offset</arg_key><arg_value>10</arg_value></tool_call>";
        TextToolCallStreamParser parser = new(Tools);

        (_, var calls) = parser.Feed(input);

        FunctionCallContent call = Assert.Single(calls);
        Assert.Equal("a.cs", call.Arguments!["filePath"]);
        Assert.Equal("10", call.Arguments!["offset"]);
    }

    /// <summary>
    /// 实录（小米 MiMo）：两次 Grep 都写成 <c>&lt;function=…&gt;&lt;parameter=…&gt;</c> 的纯文本，
    /// 服务端只解出一条 arguments 为 <c>{}</c> 的结构化调用，于是框架执行空调用、缺必填参数报错，
    /// 模型反复重试同一段文本，一轮作废。正文这条必须能恢复出真正的参数。
    /// </summary>
    [Fact]
    public void RealWorldLeak_FunctionParameterDialect_IsRecovered()
    {
        const string leaked =
            "<tool_call><function=Grep><parameter=pattern>class PresenceSession</parameter>" +
            "<parameter=path>Code/Hosts/Godot/Project/Framework</parameter></function></tool_call>" +
            "<tool_call><function=Grep><parameter=pattern>class StageDirector|class PixelZoom</parameter>" +
            "<parameter=path>Code/Hosts/Godot/Project</parameter></function></tool_call>";

        TextToolCallStreamParser parser = new(Tools);
        (string text, List<FunctionCallContent> calls) = parser.Feed(leaked);
        (string tail, List<FunctionCallContent> tailCalls) = parser.Flush();

        Assert.Equal("", text + tail); //整段都是调用，不该留半截 XML 在正文里
        Assert.Empty(tailCalls);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Equal("Grep", c.Name));
        Assert.Equal("class PresenceSession", calls[0].Arguments!["pattern"]);
        Assert.Equal("Code/Hosts/Godot/Project/Framework", calls[0].Arguments!["path"]);
        Assert.Equal("Code/Hosts/Godot/Project", calls[1].Arguments!["path"]);
    }

    /// <summary>同一个模型经常只写开标签就换下一个参数：没有 <c>&lt;/parameter&gt;</c> 也要能切</summary>
    [Fact]
    public void FunctionParameterDialect_WithoutClosingTags_StillParses()
    {
        const string leaked = "<tool_call><function=Grep><parameter=path>Code/Project" +
                              "<parameter=pattern>class PresenceSession</tool_call>";

        TextToolCallStreamParser parser = new(Tools);
        (_, List<FunctionCallContent> calls) = parser.Feed(leaked);

        FunctionCallContent call = Assert.Single(calls);
        Assert.Equal("Code/Project", call.Arguments!["path"]);
        Assert.Equal("class PresenceSession", call.Arguments!["pattern"]);
    }

    /// <summary>参数值里带 <c>&lt;</c>（正则、泛型）：有闭合标签时以闭合标签为界，别把它当标签切掉</summary>
    [Fact]
    public void FunctionParameterDialect_ValueWithAngleBracket_IsKept()
    {
        const string leaked = "<tool_call><function=Grep><parameter=pattern>List&lt;T&gt;|a<b</parameter>" +
                              "<parameter=path>src</parameter></function></tool_call>";

        TextToolCallStreamParser parser = new(Tools);
        (_, List<FunctionCallContent> calls) = parser.Feed(leaked);

        Assert.Equal("List&lt;T&gt;|a<b", Assert.Single(calls).Arguments!["pattern"]);
    }

    /// <summary>名字不命中已装配工具的 <c>&lt;function=…&gt;</c> 块同样原文放行</summary>
    [Fact]
    public void FunctionParameterDialect_UnknownTool_PassesThroughAsText()
    {
        const string input = "模板示例:<tool_call><function=NotATool><parameter=x>1</parameter></tool_call>";
        TextToolCallStreamParser parser = new(Tools);

        (string text, List<FunctionCallContent> calls) = parser.Feed(input);
        (string tail, List<FunctionCallContent> tailCalls) = parser.Flush();

        Assert.Empty(calls);
        Assert.Empty(tailCalls);
        Assert.Equal(input, text + tail);
    }
}
