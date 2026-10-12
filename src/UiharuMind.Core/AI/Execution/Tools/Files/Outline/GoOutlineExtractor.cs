using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// Go 大纲：<c>type</c> 声明（interface 的方法签名列在下一层）与 <c>func</c>（含方法接收者）。
/// 分号可省，换行当语句结束；<c>type ( … )</c> 分组声明不展开。
/// </summary>
internal sealed partial class GoOutlineExtractor : BraceOutlineExtractor
{
    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } = [".go"];

    /// <inheritdoc />
    protected override ELiteralSyntax Syntax => ELiteralSyntax.CharLiterals | ELiteralSyntax.BacktickStrings;

    /// <inheritdoc />
    protected override bool NewlineEndsStatement => true;

    /// <inheritdoc />
    protected override bool IsDirectiveLine(string code) => false;

    /// <inheritdoc />
    protected override Declaration Classify(string header, bool insideType, char terminator)
    {
        string text = Squash(header);

        // struct 字段不列；interface 的方法签名是「名字紧跟括号」
        if (insideType) return new Declaration(EScope.Opaque, InterfaceMethod().IsMatch(text) ? Shorten(text) : null);

        Match type = TypeDeclaration().Match(text);
        if (type.Success)
        {
            EScope body = type.Groups["kind"].Success ? EScope.Type : EScope.Opaque;
            return new Declaration(body, Shorten(text));
        }

        return new Declaration(EScope.Opaque, text.StartsWith("func ") ? Shorten(text) : null);
    }

    [GeneratedRegex(@"^type\s+\w+(?:\[[^\]]*\])?\s+(?:(?<kind>struct|interface)\b|\S)")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"^\w+\s*\(")]
    private static partial Regex InterfaceMethod();
}
