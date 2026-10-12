using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// C/C++ 大纲：类型（class/struct/union/enum）与函数（定义与声明，含类外定义 <c>Foo::Bar()</c>）。
/// 与 C# 同口径不列字段；头文件里的函数声明要列，那就是接口本身。
/// 宏展开不认：<c>TEST(Suite, Name) {</c> 这类形似函数的宏按函数列出，正合跳读所需。
/// </summary>
internal sealed partial class CppOutlineExtractor : BraceOutlineExtractor
{
    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } =
        [".cpp", ".cc", ".cxx", ".c++", ".c", ".hpp", ".hh", ".hxx", ".h", ".inl", ".ipp"];

    /// <inheritdoc />
    protected override ELiteralSyntax Syntax =>
        ELiteralSyntax.CharLiterals | ELiteralSyntax.CppRawStrings | ELiteralSyntax.DigitSeparators;

    // 独占一行的 public: 不该把下一个声明的行号提前
    /// <inheritdoc />
    protected override bool IsNoiseLine(string code) => AccessLabels().Match(code).Length == code.Length;

    /// <inheritdoc />
    protected override Declaration Classify(string header, bool insideType, char terminator)
    {
        // 类型体里的 public: 会粘在下一个声明头前面
        string text = AccessLabels().Replace(Squash(Attributes().Replace(header, "")), "");
        if (text.Length == 0) return new Declaration(EScope.Opaque, null);
        if (NamespaceDeclaration().IsMatch(text)) return new Declaration(EScope.Namespace, null);

        string body = StripTemplate(text);
        if (body.StartsWith("typedef ") && terminator == ';' || body.StartsWith("using ") || body.StartsWith("friend "))
            return new Declaration(EScope.Opaque, null);

        Match type = TypeDeclaration().Match(body);
        if (type.Success)
        {
            // 前置声明 class Foo; 不列；枚举体里是枚举值
            if (terminator == ';') return new Declaration(EScope.Opaque, null);
            EScope opens = type.Groups["kind"].Value == "enum" ? EScope.Opaque : EScope.Type;
            return new Declaration(opens, Shorten(text));
        }

        return new Declaration(EScope.Opaque, IsFunction(body, insideType, terminator) ? Shorten(CutInitializers(text)) : null);
    }

    // 括号先于赋值号才是函数（operator= 例外）；命名空间作用域的声明还要求有返回类型，
    // 排除 static_assert(...); 与宏调用 FOO(x);。类外定义 Foo::Foo() { 无返回类型，靠 { 放行
    private static bool IsFunction(string body, bool insideType, char terminator)
    {
        int paren = body.IndexOf('(');
        if (paren <= 0) return false;
        if (!body.Contains("operator"))
        {
            int assign = body.IndexOf('=');
            if (assign >= 0 && assign < paren) return false;
        }

        if (insideType || terminator == '{') return true;
        string beforeName = body[..paren].TrimEnd();
        return beforeName.Contains(' ') && !beforeName.StartsWith("return ");
    }

    // 构造函数的初始化列表不进大纲：Foo(int a) : _a(a), _b{b} 只留 Foo(int a)
    private static string CutInitializers(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0)
            {
                Match init = Initializers().Match(text, i + 1);
                return init.Success ? text[..(init.Index + init.Groups["keep"].Length)].TrimEnd() : text;
            }
        }

        return text;
    }

    // template <typename T, class U = std::vector<T>> 剥掉再认，否则参数里的 class 会被当成类型声明
    private static string StripTemplate(string text)
    {
        Match template = TemplatePrefix().Match(text);
        if (!template.Success) return text;

        int depth = 0;
        for (int i = template.Length - 1; i < text.Length; i++)
        {
            if (text[i] == '<') depth++;
            else if (text[i] == '>' && --depth == 0) return StripTemplate(text[(i + 1)..].TrimStart());
        }

        return text;
    }

    // [[nodiscard]]、[[deprecated("…")]]
    [GeneratedRegex(@"\[\[[^\]]*\]\]\s*")]
    private static partial Regex Attributes();

    [GeneratedRegex(@"^(?:(?:public|private|protected)\s*:\s*)+")]
    private static partial Regex AccessLabels();

    // namespace a::b、inline namespace v1、匿名 namespace、extern "C"
    [GeneratedRegex(@"^(?:(?:inline\s+)?namespace\b|extern\s*""""$)")]
    private static partial Regex NamespaceDeclaration();

    [GeneratedRegex(@"^template\s*<")]
    private static partial Regex TemplatePrefix();

    [GeneratedRegex(@"^(?:typedef\s+)?(?:(?:export|static|inline|constexpr)\s+)*(?<kind>class|struct|union|enum)\b")]
    private static partial Regex TypeDeclaration();

    // ) 之后单独的冒号（排除 ::）
    [GeneratedRegex(@"\G(?<keep>[^:]*?)(?<!:):(?!:)")]
    private static partial Regex Initializers();
}
