using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// C# 大纲：类型（class/struct/interface/enum/record）与成员（方法、构造、属性、索引器、事件、委托）。
/// 字段与常量不列——数量多、跳读价值低，列了只会把预算挤给噪音。
/// </summary>
internal sealed partial class CSharpOutlineExtractor : BraceOutlineExtractor
{
    private const int MaxTextChars = 120;

    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } = [".cs"];

    /// <inheritdoc />
    protected override CLikeSyntax Syntax { get; } = new(VerbatimStrings: true, RawStrings: true, CharLiterals: true);

    // [Fact]、[Description("…")] 这类独占一行的特性：不该攒进下一个声明头
    /// <inheritdoc />
    protected override bool IsNoiseLine(string code) =>
        base.IsNoiseLine(code) || (code.StartsWith('[') && code.EndsWith(']'));

    /// <inheritdoc />
    protected override Declaration Classify(string header, bool insideType, char terminator)
    {
        string text = Whitespace().Replace(Attributes().Replace(header, ""), " ").Trim()
            .Replace("( ", "(").Replace(" )", ")"); //参数换行折成空格后，括号内侧多出的空白
        if (text.Length == 0) return new Declaration(EScope.Opaque, null);
        if (NamespaceDeclaration().IsMatch(text)) return new Declaration(EScope.Namespace, null);

        Match type = TypeDeclaration().Match(text);
        if (type.Success)
        {
            // 枚举体里是枚举值，不是成员
            EScope body = type.Groups["kind"].Value == "enum" ? EScope.Opaque : EScope.Type;
            return new Declaration(body, Shorten(text));
        }

        // 类型体外只认委托（它也是类型），其余是顶层语句或全局 using
        if (!insideType) return new Declaration(EScope.Opaque, DelegateDeclaration().IsMatch(text) ? Shorten(text) : null);
        return new Declaration(EScope.Opaque, IsMember(text, terminator) ? Shorten(CutBody(text)) : null);
    }

    // 方法/构造/运算符：括号先于赋值号；属性：=> 或带访问器块；事件、委托按关键字。其余是字段
    private static bool IsMember(string text, char terminator)
    {
        int arrow = text.IndexOf("=>", StringComparison.Ordinal);
        int assign = AssignIndex(text);
        int paren = text.IndexOf('(');
        if (paren >= 0 && (assign < 0 || paren < assign)) return true;
        if (arrow >= 0 && (assign < 0 || arrow <= assign)) return true;
        if (terminator == '{' && assign < 0) return true;
        return EventOrDelegate().IsMatch(text);
    }

    // 单独的 = 号（排除 => == != <= >=）
    private static int AssignIndex(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '=') continue;
            char before = i > 0 ? text[i - 1] : ' ';
            char after = i + 1 < text.Length ? text[i + 1] : ' ';
            if (after is '>' or '=' || before is '=' or '!' or '<' or '>') continue;
            return i;
        }

        return -1;
    }

    // 表达式体只留签名：public int Count => _items.Count 显示成 public int Count
    private static string CutBody(string text)
    {
        int arrow = text.IndexOf("=>", StringComparison.Ordinal);
        return arrow > 0 ? text[..arrow].TrimEnd() : text;
    }

    private static string Shorten(string text) =>
        text.Length <= MaxTextChars ? text : text[..MaxTextChars] + "…";

    // 特性在哪都剥（类型前、参数前），签名才不被 [Description("…")] 挤满。
    // 特性名以字母开头，借此避开数组 int[] / [,]；this[...] 是索引器参数，排除
    [GeneratedRegex(@"(?<!\bthis\s*)\[\s*[A-Za-z_][^\]]*\]\s*")]
    private static partial Regex Attributes();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^namespace\s")]
    private static partial Regex NamespaceDeclaration();

    [GeneratedRegex(@"^(?:(?:public|private|protected|internal|static|sealed|abstract|partial|readonly|ref|unsafe|file|new)\s+)*(?<kind>class|struct|interface|enum|record)\b")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"\b(event|delegate)\s")]
    private static partial Regex EventOrDelegate();

    [GeneratedRegex(@"^(?:(?:public|internal|file|unsafe)\s+)*delegate\s")]
    private static partial Regex DelegateDeclaration();
}
