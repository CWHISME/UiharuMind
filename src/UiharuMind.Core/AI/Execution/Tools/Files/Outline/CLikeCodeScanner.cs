using System.Text;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 类 C 语言的字面量差异：各语言字符串写法不同，其余（// 与 /* */ 注释、普通字符串、字符字面量）共用
/// </summary>
/// <param name="VerbatimStrings">支持 <c>@"..."</c> 逐字字符串（可跨行，<c>""</c> 转义引号）</param>
/// <param name="RawStrings">支持 <c>"""...""" </c> 原始字符串（三个及以上引号，可跨行）</param>
/// <param name="CharLiterals">单引号是字符字面量（C++ 的数字分隔符 <c>1'000</c> 会被误认，扩展时注意）</param>
internal readonly record struct CLikeSyntax(bool VerbatimStrings, bool RawStrings, bool CharLiterals);

/// <summary>
/// 逐行剥掉类 C 语言的注释与字符串内容，只留代码骨架：字符串变成 <c>""</c>、注释删掉。
/// 跨行状态（块注释、逐字/原始字符串）记在实例上，所以一个文件用一个实例、按顺序喂行。
/// 只为认结构服务：字符串里的 <c>{</c> <c>;</c> 不能被当成代码，其余精度不追求
/// （插值字符串洞里再套引号这类罕见写法会认错）。
/// </summary>
internal sealed class CLikeCodeScanner(CLikeSyntax syntax)
{
    private enum EMode
    {
        Code,
        BlockComment,
        VerbatimString,
        RawString,
    }

    private EMode _mode = EMode.Code;
    private int _rawQuotes; //原始字符串的引号数，遇到同样长的引号串才结束

    /// <summary>
    /// 剥掉一行里的注释与字符串内容
    /// </summary>
    /// <param name="line">原始行</param>
    /// <returns>只含代码骨架的行</returns>
    public string Clean(string line)
    {
        var sb = new StringBuilder(line.Length);
        int i = 0;
        while (i < line.Length)
        {
            switch (_mode)
            {
                case EMode.BlockComment:
                    i = SkipPast(line, i, "*/");
                    break;
                case EMode.VerbatimString:
                    i = SkipVerbatim(line, i, sb);
                    break;
                case EMode.RawString:
                    i = SkipPast(line, i, new string('"', _rawQuotes));
                    if (_mode == EMode.Code) sb.Append('"');
                    break;
                default:
                    i = ScanCode(line, i, sb);
                    break;
            }
        }

        return sb.ToString();
    }

    private int ScanCode(string line, int i, StringBuilder sb)
    {
        char c = line[i];
        char next = i + 1 < line.Length ? line[i + 1] : '\0';
        if (c == '/' && next == '/') return line.Length;
        if (c == '/' && next == '*')
        {
            _mode = EMode.BlockComment;
            return i + 2;
        }

        if (c == '\'' && syntax.CharLiterals)
        {
            sb.Append("''");
            return SkipQuoted(line, i + 1, '\'');
        }

        int quote = StringStart(line, i, out bool verbatim);
        if (quote < 0)
        {
            sb.Append(c);
            return i + 1;
        }

        sb.Append('"');
        int quotes = CountRun(line, quote, '"');
        if (syntax.RawStrings && quotes >= 3)
        {
            _mode = EMode.RawString;
            _rawQuotes = quotes;
            return quote + quotes;
        }

        if (verbatim)
        {
            _mode = EMode.VerbatimString;
            return quote + 1;
        }

        sb.Append('"');
        return SkipQuoted(line, quote + 1, '"');
    }

    // 字符串起点：引号本身，或 $ @ 前缀后紧跟引号（$@"、@$"、$$"""）。返回引号位置，不是字符串返回 -1
    private int StringStart(string line, int i, out bool verbatim)
    {
        verbatim = false;
        int j = i;
        while (j < line.Length && (line[j] == '$' || (line[j] == '@' && syntax.VerbatimStrings)))
        {
            verbatim |= line[j] == '@';
            j++;
        }

        return j < line.Length && line[j] == '"' ? j : -1;
    }

    private int SkipVerbatim(string line, int i, StringBuilder sb)
    {
        while (i < line.Length)
        {
            int quote = line.IndexOf('"', i);
            if (quote < 0) return line.Length;
            if (quote + 1 < line.Length && line[quote + 1] == '"')
            {
                i = quote + 2;
                continue;
            }

            _mode = EMode.Code;
            sb.Append('"');
            return quote + 1;
        }

        return i;
    }

    private int SkipPast(string line, int i, string terminator)
    {
        int end = line.IndexOf(terminator, i, StringComparison.Ordinal);
        if (end < 0) return line.Length;
        _mode = EMode.Code;
        return end + terminator.Length;
    }

    // 普通字符串与字符字面量不跨行：没闭合就吃到行尾
    private static int SkipQuoted(string line, int i, char quote)
    {
        while (i < line.Length)
        {
            if (line[i] == '\\') i += 2;
            else if (line[i] == quote) return i + 1;
            else i++;
        }

        return line.Length;
    }

    private static int CountRun(string line, int i, char c)
    {
        int end = i;
        while (end < line.Length && line[end] == c) end++;
        return end - i;
    }
}
