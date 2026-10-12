namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 大纲里的一条结构：所在行号、嵌套深度与显示文本
/// </summary>
/// <param name="Line">行号，1 起算（与 Read 的 offset 同口径，模型可直接跳读）</param>
/// <param name="Depth">嵌套深度，0 为最外层；渲染按它缩进，超预算时先裁最深的层</param>
/// <param name="Text">显示文本，如 <c>class Foo : Bar</c>、<c>## 安装</c></param>
/// <param name="EndLine">结构的最后一行（大括号闭合处、下一个同级标题前）；0 表示未知。Grep 靠它判断命中落在哪个结构里</param>
internal readonly record struct OutlineEntry(int Line, int Depth, string Text, int EndLine = 0)
{
    /// <summary>
    /// 某行是否落在这个结构里
    /// </summary>
    /// <param name="line">行号，1 起算</param>
    /// <returns>是否包含</returns>
    public bool Contains(int line) => line >= Line && line <= EndLine;
}

/// <summary>
/// 某类文件的结构抽取器。只管「从行里认出结构」，预算、裁剪与渲染统一在 <see cref="FileOutline"/>。
/// 新增一种文件类型 = 实现本接口 + 在 <see cref="FileOutline"/> 注册一行。
/// 实现必须是纯函数、无状态（同一实例会被并发调用），且只做单遍扫描。
/// </summary>
internal interface IOutlineExtractor
{
    /// <summary>负责的扩展名，带点、小写，如 <c>.cs</c></summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// 按出现顺序抽取结构
    /// </summary>
    /// <param name="lines">文件的全部行，按顺序</param>
    /// <returns>大纲条目，行号升序</returns>
    IEnumerable<OutlineEntry> Extract(IEnumerable<string> lines);
}
