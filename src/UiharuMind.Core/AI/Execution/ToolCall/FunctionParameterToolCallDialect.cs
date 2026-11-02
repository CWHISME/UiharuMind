namespace UiharuMind.Core.AI.Execution.ToolCall;

/// <summary>
/// MiMo 线写法：<c>&lt;tool_call&gt;&lt;function=名字&gt;&lt;parameter=K&gt;V&lt;/parameter&gt;…&lt;/function&gt;&lt;/tool_call&gt;</c>。
///
/// <para>与 GLM 那套的区别不只是字段名：参数由<b>下一个</b> <c>&lt;parameter=</c> 分界，
/// 闭合标签经常整个缺失（实测小米 MiMo 写一半就换下一个参数），所以不能靠配对标签切。
/// 有闭合标签时仍以它为准——参数值里可以带 <c>&lt;</c>（正则、泛型都是）。</para>
///
/// <para>顺带一条：这个模型会把调用同时写进正文和结构化 <c>tool_calls</c>，
/// 而结构化那条的 <c>arguments</c> 是空对象，执行必然失败。恢复出正文这条才有参数可用
/// （去重见 <c>LazyChatClient</c>）。</para>
/// </summary>
internal sealed class FunctionParameterToolCallDialect : ITextToolCallDialect
{
    public static readonly FunctionParameterToolCallDialect Instance = new();

    private const string FunctionTag = "<function=";
    private const string FunctionCloseTag = "</function>";
    private const string ParameterTag = "<parameter=";
    private const string ParameterCloseTag = "</parameter>";

    private FunctionParameterToolCallDialect()
    {
    }

    public bool TryParse(string block, out string name, out Dictionary<string, object?> arguments)
    {
        name = "";
        arguments = new Dictionary<string, object?>();

        int at = block.IndexOf(FunctionTag, StringComparison.Ordinal);
        if (at < 0) return false;

        int nameStart = at + FunctionTag.Length;
        int nameEnd = nameStart;
        while (nameEnd < block.Length && block[nameEnd] != '>' && block[nameEnd] != '<') nameEnd++;
        name = block[nameStart..nameEnd].Trim().Trim('"', '\'');
        if (name.Length == 0) return false;

        List<int> starts = [];
        for (int found = block.IndexOf(ParameterTag, StringComparison.Ordinal);
             found >= 0;
             found = block.IndexOf(ParameterTag, found + ParameterTag.Length, StringComparison.Ordinal))
        {
            starts.Add(found);
        }

        for (int index = 0; index < starts.Count; index++)
        {
            int keyStart = starts[index] + ParameterTag.Length;
            int keyEnd = block.IndexOf('>', keyStart);
            if (keyEnd < 0) break; // 残块：连参数的 '>' 都没写完，后面的都不可信

            string key = block[keyStart..keyEnd].Trim().Trim('"', '\'');
            int valueStart = keyEnd + 1;
            int valueEnd = index + 1 < starts.Count ? starts[index + 1] : block.Length;

            int closed = block.IndexOf(ParameterCloseTag, valueStart, StringComparison.Ordinal);
            if (closed >= 0 && closed < valueEnd) valueEnd = closed;

            if (key.Length > 0) arguments[key] = TrimValue(block[valueStart..valueEnd]);
        }

        return true;
    }

    /// <summary>去掉参数值尾部可能残留的 <c>&lt;/function&gt;</c>（块尾那一个参数会带上）</summary>
    private static string TrimValue(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.EndsWith(FunctionCloseTag, StringComparison.Ordinal))
        {
            trimmed = trimmed[..^FunctionCloseTag.Length].Trim();
        }

        return trimmed;
    }
}
