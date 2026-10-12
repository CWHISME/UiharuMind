using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// JS/TS 大纲：类、接口、枚举，顶层函数与赋成函数的常量（<c>const Foo = () =&gt; {</c>），类与接口里的方法。
/// 只认声明：顶层的调用（<c>describe('…', () =&gt; {</c>、<c>app.get(…)</c>）不列，字符串已被剥空，列出来也看不出是哪个。
/// 分号可省，换行当语句结束（类型声明头除外，它必带大括号）；大括号另起一行的函数只认到签名为止。
/// 正则字面量按前一个字符猜，<c>return /re/</c> 会认成除号。
/// </summary>
internal sealed partial class TypeScriptOutlineExtractor : BraceOutlineExtractor
{
    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } =
        [".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs"];

    /// <inheritdoc />
    protected override ELiteralSyntax Syntax =>
        ELiteralSyntax.CharLiterals | ELiteralSyntax.BacktickStrings | ELiteralSyntax.RegexLiterals;

    /// <inheritdoc />
    protected override bool NewlineEndsStatement => true;

    // # 开头是私有成员，不是指令；只有 #! 解释器行要跳
    /// <inheritdoc />
    protected override bool IsDirectiveLine(string code) => code.StartsWith("#!");

    // 类、接口、枚举、命名空间必带大括号：跨行写的声明头（extends 换行、泛型参数换行）不能在行尾截断
    /// <inheritdoc />
    protected override bool AwaitsBody(string header)
    {
        string text = Squash(Decorators().Replace(header, ""));
        return TypeDeclaration().IsMatch(text) || NamespaceDeclaration().IsMatch(text);
    }

    /// <inheritdoc />
    protected override bool IsNoiseLine(string code) => code.StartsWith('@') && Decorators().Replace(code, "").Length == 0;

    /// <inheritdoc />
    protected override Declaration Classify(string header, bool insideType, char terminator)
    {
        string text = Squash(Decorators().Replace(header, ""));
        if (text.Length == 0) return new Declaration(EScope.Opaque, null);
        if (NamespaceDeclaration().IsMatch(text)) return new Declaration(EScope.Namespace, null);

        Match type = TypeDeclaration().Match(text);
        if (type.Success)
        {
            if (terminator == ';') return new Declaration(EScope.Opaque, null);
            EScope body = type.Groups["kind"].Value == "enum" ? EScope.Opaque : EScope.Type;
            return new Declaration(body, Shorten(text));
        }

        bool listed = insideType
            ? Method().IsMatch(text) || ArrowField().IsMatch(text)
            : FunctionDeclaration().IsMatch(text) || FunctionConstant().IsMatch(text);
        return new Declaration(EScope.Opaque, listed ? Shorten(CutArrowBody(text)) : null);
    }

    [GeneratedRegex(@"@[\w.]+(?:\([^)]*\))?\s*")]
    private static partial Regex Decorators();

    [GeneratedRegex(@"^(?:export\s+)?(?:declare\s+)?(?:namespace|module|global)\b")]
    private static partial Regex NamespaceDeclaration();

    [GeneratedRegex(@"^(?:export\s+)?(?:default\s+)?(?:declare\s+)?(?:abstract\s+)?(?:const\s+)?(?<kind>class|interface|enum)\b")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"^(?:export\s+)?(?:default\s+)?(?:declare\s+)?(?:async\s+)?function\b")]
    private static partial Regex FunctionDeclaration();

    // const foo = (…) =>、const foo = async x =>、const foo = function
    [GeneratedRegex(@"^(?:export\s+)?(?:const|let|var)\s+[\w$]+\s*(?::[^=]+)?=\s*(?:async\s+)?(?:function\b|\(.*\)\s*(?::[^=]+)?=>|[\w$]+\s*=>)")]
    private static partial Regex FunctionConstant();

    // 方法、构造、访问器与接口里的方法签名：名字后紧跟（可选泛型）括号
    [GeneratedRegex(@"^(?:(?:public|private|protected|static|readonly|abstract|async|override|declare|get|set|accessor)\s+)*\*?[#\w$]+\??\s*(?:<[^>]*>)?\s*\(")]
    private static partial Regex Method();

    // 类字段赋箭头函数：handle = (e) => {
    [GeneratedRegex(@"^(?:(?:public|private|protected|static|readonly)\s+)*[#\w$]+\s*(?::[^=]+)?=\s*(?:async\s+)?(?:\(.*\)|[\w$]+)\s*(?::[^=]+)?=>")]
    private static partial Regex ArrowField();
}
