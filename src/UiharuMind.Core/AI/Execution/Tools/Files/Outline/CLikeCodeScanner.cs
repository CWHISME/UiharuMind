using System.Text;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 类 C 语言的字面量差异。<c>//</c> 与 <c>/* */</c> 注释、双引号字符串各语言共用，其余按位组合
/// </summary>
[Flags]
internal enum ELiteralSyntax
{
    None = 0,

    /// <summary>单引号是字符字面量（JS 里是字符串，扫描上一样）</summary>
    CharLiterals = 1 << 0,

    /// <summary>C# <c>@"..."</c> 逐字字符串（可跨行，<c>""</c> 转义引号）</summary>
    VerbatimStrings = 1 << 1,

    /// <summary>C# 原始字符串、Java 文本块：三个及以上引号起止，可跨行</summary>
    TripleQuoteStrings = 1 << 2,

    /// <summary>C++ <c>R"delim(...)delim"</c> 原始字符串（可跨行）</summary>
    CppRawStrings = 1 << 3,

    /// <summary>C++ 数字分隔符 <c>1'000</c>：数字后的单引号不是字符字面量</summary>
    DigitSeparators = 1 << 4,

    /// <summary>反引号字符串，可跨行（JS 模板字符串、Go 原始字符串）。模板洞里再套反引号会认错</summary>
    BacktickStrings = 1 << 5,

    /// <summary>JS 正则字面量 <c>/[{]/</c>：按前一个字符猜是正则还是除号</summary>
    RegexLiterals = 1 << 6,

    /// <summary>Rust：<c>'a</c> 生命周期不是字符字面量，<c>r#"..."#</c> 原始字符串，普通字符串可跨行</summary>
    RustLiterals = 1 << 7,
}

/// <summary>
/// 逐行剥掉类 C 语言的注释与字符串内容，只留代码骨架：字符串变成 <c>""</c>、注释删掉。
/// 跨行状态（块注释、跨行字符串）记在实例上，所以一个文件用一个实例、按顺序喂行。
/// 只为认结构服务：字符串里的 <c>{</c> <c>;</c> 不能被当成代码，其余精度不追求
/// （插值字符串洞里再套引号这类罕见写法会认错）。
/// </summary>
internal sealed class CLikeCodeScanner(ELiteralSyntax syntax)
{
    private enum EMode
    {
        Code,
        BlockComment,
        VerbatimString,
        RawString,
        QuotedString,
    }

    private EMode _mode = EMode.Code;
    private string _rawTerminator = ""; //原始字符串的结束串：C# 是同样长的引号串，C++ 是 )delim"，Rust 是 "##

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
                    i = SkipPast(line, i, _rawTerminator);
                    if (_mode == EMode.Code) sb.Append('"');
                    break;
                case EMode.QuotedString:
                    i = SkipQuoted(line, i, '"');
                    if (_mode == EMode.Code) sb.Append('"');
                    break;
                default:
                    i = ScanCode(line, i, sb);
                    break;
            }
        }

        return sb.ToString();
    }

    private bool Has(ELiteralSyntax flag) => (syntax & flag) != 0;

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

        if (c == '/' && Has(ELiteralSyntax.RegexLiterals) && StartsRegex(sb))
        {
            sb.Append("//");
            return SkipRegex(line, i + 1);
        }

        if (c == '`' && Has(ELiteralSyntax.BacktickStrings)) return OpenRaw(sb, "`", i + 1);

        if (c == '\'')
        {
            int skipped = ScanSingleQuote(line, i, sb);
            if (skipped > 0) return skipped;
        }

        int rawBody = RawStringBody(line, i, out string terminator);
        if (rawBody > 0) return OpenRaw(sb, terminator, rawBody);

        int quote = StringStart(line, i, out bool verbatim);
        if (quote < 0)
        {
            sb.Append(c);
            return i + 1;
        }

        int quotes = CountRun(line, quote, '"');
        if (Has(ELiteralSyntax.TripleQuoteStrings) && quotes >= 3) return OpenRaw(sb, new string('"', quotes), quote + quotes);

        sb.Append('"');
        if (verbatim)
        {
            _mode = EMode.VerbatimString;
            return quote + 1;
        }

        // Rust 普通字符串可跨行：没闭合就带着状态进下一行
        if (Has(ELiteralSyntax.RustLiterals)) _mode = EMode.QuotedString;
        int end = SkipQuoted(line, quote + 1, '"');
        if (_mode == EMode.Code) sb.Append('"');
        return end;
    }

    private int OpenRaw(StringBuilder sb, string terminator, int bodyStart)
    {
        sb.Append('"');
        _mode = EMode.RawString;
        _rawTerminator = terminator;
        return bodyStart;
    }

    // 单引号：字符字面量、数字分隔符或 Rust 生命周期。返回跳过后的位置，当普通字符处理时返回 -1
    private int ScanSingleQuote(string line, int i, StringBuilder sb)
    {
        if (Has(ELiteralSyntax.DigitSeparators) && IsDigitSeparator(line, i)) return -1;
        if (Has(ELiteralSyntax.RustLiterals))
        {
            // 'x' 与 '\n' 是字符，'a 是生命周期
            bool isChar = i + 1 < line.Length && (line[i + 1] == '\\' || (i + 2 < line.Length && line[i + 2] == '\''));
            if (!isChar) return -1;
        }
        else if (!Has(ELiteralSyntax.CharLiterals))
        {
            return -1;
        }

        sb.Append("''");
        return SkipQuoted(line, i + 1, '\'');
    }

    // 字符串起点：引号本身，或 $ @ 前缀后紧跟引号（$@"、@$"、$$"""）。返回引号位置，不是字符串返回 -1
    private int StringStart(string line, int i, out bool verbatim)
    {
        verbatim = false;
        int j = i;
        while (j < line.Length && (line[j] == '$' || (line[j] == '@' && Has(ELiteralSyntax.VerbatimStrings))))
        {
            verbatim |= line[j] == '@';
            j++;
        }

        return j < line.Length && line[j] == '"' ? j : -1;
    }

    // C++ R"delim( 与 Rust r#"：返回正文起点并给出结束串，不是返回 -1
    private int RawStringBody(string line, int i, out string terminator)
    {
        terminator = "";
        if (Has(ELiteralSyntax.CppRawStrings) && line[i] == '"' && i > 0 && line[i - 1] == 'R'
            && IdentifierBefore(line, i) is "R" or "LR" or "uR" or "UR" or "u8R")
        {
            int paren = line.IndexOf('(', i + 1);
            if (paren < 0 || paren - i - 1 > 16) return -1; //分隔符最长 16 字符
            terminator = ")" + line[(i + 1)..paren] + "\"";
            return paren + 1;
        }

        if (Has(ELiteralSyntax.RustLiterals) && line[i] == 'r' && (i == 0 || !IsIdentifierChar(line[i - 1])
                                                                  || (line[i - 1] == 'b' && (i == 1 || !IsIdentifierChar(line[i - 2])))))
        {
            int hashes = CountRun(line, i + 1, '#');
            int quote = i + 1 + hashes;
            if (quote >= line.Length || line[quote] != '"') return -1;
            terminator = "\"" + new string('#', hashes);
            return quote + 1;
        }

        return -1;
    }

    // 前一个代码字符是运算符、开括号或行首时，/ 起的是正则而不是除号
    private static bool StartsRegex(StringBuilder sb)
    {
        for (int k = sb.Length - 1; k >= 0; k--)
        {
            if (char.IsWhiteSpace(sb[k])) continue;
            return "(,=:[!&|?{};+-*%~^".Contains(sb[k]);
        }

        return true;
    }

    private static int SkipRegex(string line, int i)
    {
        bool inClass = false;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '\\') i += 2;
            else
            {
                if (c == '[') inClass = true;
                else if (c == ']') inClass = false;
                else if (c == '/' && !inClass) return i + 1;
                i++;
            }
        }

        return line.Length;
    }

    // 1'000、0xFF'FF：单引号前是以数字开头的字面量。u8'a' 这类前缀以字母开头，不算
    private static bool IsDigitSeparator(string line, int i)
    {
        int start = i;
        while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] is '\'' or '.')) start--;
        return start < i && char.IsDigit(line[start]);
    }

    private static string IdentifierBefore(string line, int end)
    {
        int start = end;
        while (start > 0 && IsIdentifierChar(line[start - 1])) start--;
        return line[start..end];
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

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

    // 普通字符串与字符字面量：闭合时回到代码态；没闭合吃到行尾（跨行字符串的状态由调用方先设好）
    private int SkipQuoted(string line, int i, char quote)
    {
        while (i < line.Length)
        {
            if (line[i] == '\\') i += 2;
            else if (line[i] == quote)
            {
                _mode = EMode.Code;
                return i + 1;
            }
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
