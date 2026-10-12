using System.Text;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 大括号语言（C#、C++、Java、JS…）的大纲骨架（模板方法）：剥注释字符串、维护大括号栈、
/// 把两个 <c>{ ; }</c> 之间的代码攒成一段「声明头」，交给子类 <see cref="Classify"/> 认是什么。
/// 子类只回答「这段头是不是值得列的声明、它的大括号里装的是什么」，不碰扫描。
///
/// 只在命名空间/类型体里认声明；进了方法体、初始化器这类「不透明」的大括号就只数括号不认结构，
/// 局部函数、lambda 因此不会混进大纲。
/// </summary>
internal abstract class BraceOutlineExtractor : IOutlineExtractor
{
    private const int MaxHeaderChars = 1000; //声明头攒到这么长就不再追加（巨型初始化器之类），防正则吃大串

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
    protected abstract CLikeSyntax Syntax { get; }

    /// <summary>
    /// 认一段声明头
    /// </summary>
    /// <param name="header">两个 <c>{ ; }</c> 之间的代码，已去注释、字符串内容，换行折成空格</param>
    /// <param name="insideType">是否在类型体里（决定认不认成员）</param>
    /// <param name="terminator">以什么结束：<c>{</c> 或 <c>;</c></param>
    /// <returns>认定结果</returns>
    protected abstract Declaration Classify(string header, bool insideType, char terminator);

    /// <summary>不参与声明头的整行（预处理指令等），子类可追加（如 C# 独占一行的特性）</summary>
    /// <param name="code">去注释后的行，已 Trim</param>
    /// <returns>是否跳过</returns>
    protected virtual bool IsNoiseLine(string code) => code.StartsWith('#');

    /// <inheritdoc />
    public IEnumerable<OutlineEntry> Extract(IEnumerable<string> lines)
    {
        var scanner = new CLikeCodeScanner(Syntax);
        var scopes = new Stack<EScope>();
        var header = new StringBuilder();
        var entries = new List<OutlineEntry>();
        int typeDepth = 0;
        int opaqueDepth = 0;
        int headerLine = 0;
        int lineNumber = 0;

        foreach (string line in lines)
        {
            lineNumber++;
            string code = scanner.Clean(line);
            if (opaqueDepth == 0 && header.Length == 0 && IsNoiseLine(code.Trim())) continue;

            foreach (char c in code)
            {
                switch (c)
                {
                    case '{':
                        Open();
                        break;
                    case '}':
                        Close();
                        break;
                    case ';':
                        if (opaqueDepth == 0) Finish(';');
                        break;
                    default:
                        if (opaqueDepth > 0 || (header.Length == 0 && char.IsWhiteSpace(c))) break;
                        if (header.Length == 0) headerLine = lineNumber;
                        if (header.Length < MaxHeaderChars) header.Append(c);
                        break;
                }
            }

            if (opaqueDepth == 0 && header.Length > 0) header.Append(' ');
        }

        return entries;

        void Open()
        {
            if (opaqueDepth > 0)
            {
                scopes.Push(EScope.Opaque);
                opaqueDepth++;
                return;
            }

            EScope opens = Finish('{');
            scopes.Push(opens);
            if (opens == EScope.Type) typeDepth++;
            else if (opens == EScope.Opaque) opaqueDepth++;
        }

        void Close()
        {
            header.Clear();
            if (scopes.Count == 0) return; //括号不配对（截断的文件、宏）就忍着，不让大纲整份作废
            EScope closed = scopes.Pop();
            if (closed == EScope.Type) typeDepth--;
            else if (closed == EScope.Opaque) opaqueDepth--;
        }

        EScope Finish(char terminator)
        {
            string text = header.ToString().Trim();
            header.Clear();
            if (text.Length == 0) return EScope.Opaque;

            Declaration declaration = Classify(text, typeDepth > 0, terminator);
            if (declaration.Text != null) entries.Add(new OutlineEntry(headerLine, typeDepth, declaration.Text));
            return declaration.Opens;
        }
    }
}
