using Microsoft.CodeAnalysis;

namespace UiharuMind.Localization.Generator;

internal static class Diagnostics
{
    private const string Category = "UiharuMind.Localization.Generator";

    /// <summary>资源文件无法解析（XML 损坏、无 data 条目等）。</summary>
    public static readonly DiagnosticDescriptor ResxParseError = new(
        id: "LK1001",
        title: "语言资源文件解析失败",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>key 不是合法 C# 标识符，无法按 identity mapping 生成常量。</summary>
    public static readonly DiagnosticDescriptor InvalidKeyIdentifier = new(
        id: "LK1002",
        title: "语言 key 不是合法的 C# 标识符",
        messageFormat: "key '{0}' 不是合法的 C# 标识符，无法生成强类型常量（identity mapping 要求常量名即 key 字符串）",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>卫星语言文件相比默认语言文件缺 key（漏翻）。</summary>
    public static readonly DiagnosticDescriptor MissingInCulture = new(
        id: "LK1003",
        title: "卫星语言文件缺少 key",
        messageFormat: "culture '{0}' 缺少默认语言文件中的 {1} 个 key（如: {2}）",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>卫星语言文件比默认语言文件多出 key。</summary>
    public static readonly DiagnosticDescriptor ExtraInCulture = new(
        id: "LK1004",
        title: "卫星语言文件存在多余 key",
        messageFormat: "culture '{0}' 比默认语言文件多 {1} 个 key（如: {2}）",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>axaml 里 {loc:Loc X} 的 X 不存在于 LangKey 枚举（拼写错误）。</summary>
    public static readonly DiagnosticDescriptor MissingKeyInXaml = new(
        id: "LK2001",
        title: "XAML 引用了不存在的语言 key",
        messageFormat: "key '{0}' 不存在于 LangKey 枚举（请检查拼写或先在资源文件中补 key）",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>LangKey 成员从未被 XAML/C# 引用（死 key 候选）。</summary>
    public static readonly DiagnosticDescriptor UnusedKey = new(
        id: "LK2002",
        title: "语言 key 未被任何 XAML/C# 引用",
        messageFormat: "有 {0} 个 key 从未被引用: {1}{2}（可能有动态拼接等扫描范围外的用法，需人工确认）",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}