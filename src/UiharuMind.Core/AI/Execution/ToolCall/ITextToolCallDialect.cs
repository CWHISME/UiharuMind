namespace UiharuMind.Core.AI.Execution.ToolCall;

/// <summary>
/// 文本工具调用的一种写法。
///
/// <para>各家模板不同，语义却只有一个：调哪个函数、给哪些参数。字段名没有任何统一来源
/// （GLM 用 <c>arg_key/arg_value</c>，MiMo 用 <c>function=/parameter=</c>），
/// 所以按写法分派——加一种写法只需实现本接口并登记进 <see cref="TextToolCallDialects.All"/>，
/// 解析器本身不必改。</para>
/// </summary>
internal interface ITextToolCallDialect
{
    /// <summary>
    /// 从 <c>&lt;tool_call&gt;</c> 与 <c>&lt;/tool_call&gt;</c> 之间的块里解出调用
    /// </summary>
    /// <param name="block">块内文本（不含首尾标签）</param>
    /// <param name="name">函数名</param>
    /// <param name="arguments">参数；这个写法不带参数时为空字典</param>
    /// <returns>形状对得上为 true。名字是否命中已装配工具由调用方判断，这里不管</returns>
    bool TryParse(string block, out string name, out Dictionary<string, object?> arguments);
}

/// <summary>已支持的写法，按特异性从高到低排列：先试结构最明确的，再试宽松的</summary>
internal static class TextToolCallDialects
{
    public static readonly IReadOnlyList<ITextToolCallDialect> All =
        [ArgKeyToolCallDialect.Instance, FunctionParameterToolCallDialect.Instance];
}
