using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// Java 大纲：类型（class/interface/enum/record/@interface）与方法、构造。字段、初始化块不列，口径同 C#
/// </summary>
internal sealed partial class JavaOutlineExtractor : BraceOutlineExtractor
{
    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } = [".java"];

    /// <inheritdoc />
    protected override ELiteralSyntax Syntax => ELiteralSyntax.CharLiterals | ELiteralSyntax.TripleQuoteStrings;

    /// <inheritdoc />
    protected override bool IsDirectiveLine(string code) => false;

    // @Override 这类独占一行的注解
    /// <inheritdoc />
    protected override bool IsNoiseLine(string code) => code.StartsWith('@') && Annotations().Replace(code, "").Length == 0;

    /// <inheritdoc />
    protected override Declaration Classify(string header, bool insideType, char terminator)
    {
        string text = Squash(Annotations().Replace(header, ""));
        if (text.Length == 0) return new Declaration(EScope.Opaque, null);

        Match type = TypeDeclaration().Match(text);
        if (type.Success)
        {
            EScope body = type.Groups["kind"].Value == "enum" ? EScope.Opaque : EScope.Type;
            return new Declaration(body, Shorten(text));
        }

        // 方法与构造：括号先于赋值号。字段初始化里的 new Foo() { … } 匿名类，赋值号在前，不算
        if (!insideType) return new Declaration(EScope.Opaque, null);
        int paren = text.IndexOf('(');
        int assign = text.IndexOf('=');
        bool isMethod = paren > 0 && (assign < 0 || paren < assign);
        return new Declaration(EScope.Opaque, isMethod ? Shorten(text) : null);
    }

    // @interface 是注解类型的声明，不当注解剥
    [GeneratedRegex(@"@(?!interface\b)[\w.]+(?:\([^)]*\))?\s*")]
    private static partial Regex Annotations();

    [GeneratedRegex(@"^(?:(?:public|private|protected|static|final|abstract|sealed|non-sealed|strictfp)\s+)*(?<kind>class|interface|enum|record|@interface)\b")]
    private static partial Regex TypeDeclaration();
}
