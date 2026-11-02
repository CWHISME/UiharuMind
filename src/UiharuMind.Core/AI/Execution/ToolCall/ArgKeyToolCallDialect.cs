using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.ToolCall;

/// <summary>
/// GLM 线写法：<c>&lt;tool_call&gt;名字&lt;arg_key&gt;K&lt;/arg_key&gt;&lt;arg_value&gt;V&lt;/arg_value&gt;&lt;/tool_call&gt;</c>。
/// 名字裸写在块首，参数成对出现。
/// </summary>
internal sealed class ArgKeyToolCallDialect : ITextToolCallDialect
{
    public static readonly ArgKeyToolCallDialect Instance = new();

    private static readonly Regex PairRegex = new(
        "<arg_key>(?<key>.*?)</arg_key>\\s*<arg_value>(?<value>.*?)</arg_value>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private ArgKeyToolCallDialect()
    {
    }

    public bool TryParse(string block, out string name, out Dictionary<string, object?> arguments)
    {
        arguments = new Dictionary<string, object?>();
        int firstArg = block.IndexOf("<arg_key>", StringComparison.Ordinal);
        name = (firstArg < 0 ? block : block[..firstArg]).Trim();
        if (name.Length == 0) return false;
        if (firstArg < 0) return true;

        foreach (Match match in PairRegex.Matches(block, firstArg))
        {
            arguments[match.Groups["key"].Value.Trim()] = match.Groups["value"].Value;
        }

        return true;
    }
}
