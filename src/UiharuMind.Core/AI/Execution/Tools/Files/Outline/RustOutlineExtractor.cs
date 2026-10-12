using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// Rust 大纲：struct/enum/union/trait/impl 与其中的 <c>fn</c>，顶层 <c>fn</c> 与 <c>macro_rules!</c>。
/// <c>mod x { … }</c> 与 <c>extern "C" { … }</c> 不缩进；<c>#[…]</c> 属性整行跳过。
/// </summary>
internal sealed partial class RustOutlineExtractor : BraceOutlineExtractor
{
    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } = [".rs"];

    /// <inheritdoc />
    protected override ELiteralSyntax Syntax => ELiteralSyntax.RustLiterals;

    /// <inheritdoc />
    protected override Declaration Classify(string header, bool insideType, char terminator)
    {
        // 跨行的属性只跳了首行，余下半截会粘在声明前：从第一个条目关键字起算
        string text = Squash(header);
        Match item = Item().Match(text);
        if (!item.Success) return new Declaration(EScope.Opaque, null);

        text = text[item.Index..];
        string kind = item.Groups["kind"].Value;
        if (kind is "mod" or "extern") return new Declaration(terminator == '{' ? EScope.Namespace : EScope.Opaque, null);
        if (kind is "fn" or "macro_rules!") return new Declaration(EScope.Opaque, Shorten(text));
        if (insideType) return new Declaration(EScope.Opaque, null);

        // struct Foo(i32); 与 struct Unit; 也是类型；枚举体里是变体
        EScope body = kind == "enum" || terminator == ';' ? EScope.Opaque : EScope.Type;
        return new Declaration(body, Shorten(text));
    }

    [GeneratedRegex(@"(?<![\w:])(?:pub(?:\([^)]*\))?\s+)?(?:(?:default|const|async|unsafe|extern\s+""""|auto)\s+)*(?<kind>fn|struct|enum|union|trait|impl|mod|macro_rules!|extern)(?![\w!])")]
    private static partial Regex Item();
}
