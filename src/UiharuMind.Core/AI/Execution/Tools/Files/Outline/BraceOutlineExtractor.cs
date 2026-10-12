using System.Text;
using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 大括号语言（C#、C++、Java、JS…）的大纲骨架（模板方法）：剥注释字符串、维护大括号栈、
/// 把两个 <c>{ ; }</c> 之间的代码攒成一段「声明头」，交给子类 <see cref="Classify"/> 认是什么。
/// 子类只回答「这段头是不是值得列的声明、它的大括号里装的是什么」，不碰扫描。
///
/// 只在命名空间/类型体里认声明；进了方法体、初始化器这类「不透明」的大括号就只数括号不认结构，
/// 局部函数、lambda 因此不会混进大纲。
/// </summary>
internal abstract partial class BraceOutlineExtractor : IOutlineExtractor
{
    private const int MaxHeaderChars = 1000;
    private const int MaxTextChars = 120; //声明头攒到这么长就不再追加（巨型初始化器之类），防正则吃大串

    /// <summary>大括号里装的是什么</summary>
    protected enum EScope
    {
        /// <summary>命名空间：里面继续认声明，但不加深缩进</summary>
        Namespace,

        /// <summary>类型体：里面认成员，缩进加一层</summary>
        Type,

        /// <summary>方法体、枚举体、初始化器等：里面不认结构</summary>
        Opaque,
    }

    /// <summary>一段声明头的认定结果</summary>
    /// <param name="Opens">若以 <c>{</c> 结尾，这对大括号里装的是什么</param>
    /// <param name="Text">大纲显示文本；null 表示不列（字段、语句等）</param>
    protected readonly record struct Declaration(EScope Opens, string? Text);

    /// <inheritdoc />
    public abstract IReadOnlyList<string> Extensions { get; }

    /// <summary>这门语言的字面量写法</summary>
    protected abstract ELiteralSyntax Syntax { get; }

    /// <summary>换行即结束语句（Go、JS 不写分号）：行尾不像没写完时，当作遇到 <c>;</c></summary>
    protected virtual bool NewlineEndsStatement => false;

    /// <summary>
    /// 认一段声明头
    /// </summary>
    /// <param name="header">两个 <c>{ ; }</c> 之间的代码，已去注释、字符串内容，换行折成空格</param>
    /// <param name="insideType">是否在类型体里（决定认不认成员）</param>
    /// <param name="terminator">以什么结束：<c>{</c> 或 <c>;</c></param>
    /// <returns>认定结果</returns>
    protected abstract Declaration Classify(string header, bool insideType, char terminator);

    /// <summary>
    /// 换行结束语句时，这段声明头是否还在等它的大括号（<c>class Foo</c> 换行写 <c>extends Bar {</c>）
    /// </summary>
    /// <param name="header">攒到当前行尾的声明头</param>
    /// <returns>是否继续往下攒</returns>
    protected virtual bool AwaitsBody(string header) => false;

    /// <summary>
    /// 括号外的这个 <c>{</c> 是不是声明头里的表达式（C# 表达式体里的属性模式、字段的对象初始化器），而不是开块
    /// </summary>
    /// <param name="header">攒到 <c>{</c> 之前的声明头</param>
    /// <returns>是否算声明头的一部分</returns>
    protected virtual bool OpensInline(StringBuilder header) => false;

    /// <summary>整行跳过、连括号都不数的指令行（预处理指令、Rust 属性）；以 \ 结尾时续行也跳过</summary>
    /// <param name="code">去注释后的行，已 Trim</param>
    /// <returns>是否指令行</returns>
    protected virtual bool IsDirectiveLine(string code) => code.StartsWith('#');

    /// <summary>声明头前不参与认定的整行（如 C# 独占一行的特性）</summary>
    /// <param name="code">去注释后的行，已 Trim</param>
    /// <returns>是否跳过</returns>
    protected virtual bool IsNoiseLine(string code) => false;

    /// <summary>
    /// 显示文本过长时截断
    /// </summary>
    /// <param name="text">显示文本</param>
    /// <returns>不超过上限的文本</returns>
    protected static string Shorten(string text) =>
        text.Length <= MaxTextChars ? text : text[..MaxTextChars] + "…";

    /// <summary>
    /// 声明头折成一行：连续空白并成一个，参数、泛型换行折成空格后括号内侧多出的空白去掉
    /// </summary>
    /// <param name="text">声明头</param>
    /// <returns>折好的文本</returns>
    protected static string Squash(string text) =>
        Whitespace().Replace(text, " ").Trim().Replace("( ", "(").Replace(" )", ")").Replace("< ", "<").Replace(" >", ">");

    /// <summary>
    /// 截掉箭头后的表达式体：<c>Count => _items.Count</c> 只留 <c>Count</c>
    /// </summary>
    /// <param name="text">声明文本</param>
    /// <returns>签名部分</returns>
    protected static string CutArrowBody(string text)
    {
        int arrow = text.IndexOf("=>", StringComparison.Ordinal);
        return arrow > 0 ? text[..arrow].TrimEnd() : text;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <inheritdoc />
    public IEnumerable<OutlineEntry> Extract(IEnumerable<string> lines)
    {
        var scanner = new CLikeCodeScanner(Syntax);
        var scopes = new Stack<(EScope Scope, int Entry)>(); //Entry：这对大括号所属的大纲条目下标，-1 表示不列
        var header = new StringBuilder();
        var entries = new List<OutlineEntry>();
        int typeDepth = 0;
        int opaqueDepth = 0;
        int headerLine = 0;
        int lineNumber = 0;
        bool continued = false; //上一行是以 \ 续行的预处理指令
        int parenDepth = 0; //声明头里没闭合的 ( [ 数，不受声明头长度上限影响
        int inlineBraces = 0; //括号里的 { }（JS 解构参数、C# 对象初始化器）属于声明头，不开块

        foreach (string line in lines)
        {
            lineNumber++;
            string code = scanner.Clean(line);
            string trimmed = code.Trim();
            if (continued || IsDirectiveLine(trimmed))
            {
                // #define 宏体里的括号不配对是常事，整条指令（含续行）都不进扫描
                continued = trimmed.EndsWith('\\');
                continue;
            }

            if (opaqueDepth == 0 && header.Length == 0 && IsNoiseLine(trimmed)) continue;

            foreach (char c in code)
            {
                switch (c)
                {
                    case '{' when opaqueDepth == 0 && (parenDepth > 0 || inlineBraces > 0 || OpensInline(header)):
                        inlineBraces++;
                        AppendHeader(c);
                        break;
                    case '}' when inlineBraces > 0:
                        inlineBraces--;
                        AppendHeader(c);
                        break;
                    case '{':
                        Open();
                        break;
                    case '}':
                        Close();
                        break;
                    case ';':
                        if (opaqueDepth == 0 && inlineBraces == 0) Finish(';');
                        break;
                    default:
                        if (opaqueDepth > 0 || (header.Length == 0 && char.IsWhiteSpace(c))) break;
                        if (c is '(' or '[') parenDepth++;
                        else if (c is ')' or ']' && parenDepth > 0) parenDepth--;
                        AppendHeader(c);
                        break;
                }
            }

            if (opaqueDepth == 0 && header.Length > 0)
            {
                if (NewlineEndsStatement && parenDepth == 0 && !EndsWithOperator(header) && !AwaitsBody(header.ToString()))
                {
                    Finish(';');
                }
                else header.Append(' ');
            }
        }

        // 没闭合的（截断的文件）延到文件末尾
        foreach ((EScope _, int entry) in scopes)
        {
            if (entry >= 0) entries[entry] = entries[entry] with { EndLine = lineNumber };
        }

        return entries;

        void Open()
        {
            if (opaqueDepth > 0)
            {
                scopes.Push((EScope.Opaque, -1));
                opaqueDepth++;
                return;
            }

            (EScope opens, int entry) = Finish('{');
            scopes.Push((opens, entry));
            if (opens == EScope.Type) typeDepth++;
            else if (opens == EScope.Opaque) opaqueDepth++;
        }

        void Close()
        {
            ResetHeader();
            if (scopes.Count == 0) return; //括号不配对（截断的文件、宏）就忍着，不让大纲整份作废
            (EScope closed, int entry) = scopes.Pop();
            if (entry >= 0) entries[entry] = entries[entry] with { EndLine = lineNumber };
            if (closed == EScope.Type) typeDepth--;
            else if (closed == EScope.Opaque) opaqueDepth--;
        }

        void AppendHeader(char c)
        {
            if (header.Length == 0) headerLine = lineNumber;
            if (header.Length < MaxHeaderChars) header.Append(c);
        }

        void ResetHeader()
        {
            header.Clear();
            parenDepth = 0;
            inlineBraces = 0;
        }

        // 行尾是运算符或逗号：语句还没写完
        static bool EndsWithOperator(StringBuilder text)
        {
            int last = text.Length - 1;
            while (last >= 0 && char.IsWhiteSpace(text[last])) last--;
            return last >= 0 && ",=+-*/%&|^<>!?:.".Contains(text[last]);
        }

        // 以 ; 结束的声明到此为止；以 { 开头的等闭合时再补结束行
        (EScope Opens, int Entry) Finish(char terminator)
        {
            string text = header.ToString().Trim();
            ResetHeader();
            if (text.Length == 0) return (EScope.Opaque, -1);

            Declaration declaration = Classify(text, typeDepth > 0, terminator);
            if (declaration.Text == null) return (declaration.Opens, -1);

            entries.Add(new OutlineEntry(headerLine, typeDepth, declaration.Text, lineNumber));
            return (declaration.Opens, entries.Count - 1);
        }
    }
}
