/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>搜索失败的种类</summary>
public enum ESearchFailureKind
{
    /// <summary>搜索根目录不存在</summary>
    DirectoryNotFound,

    /// <summary>glob 表达式非法（语法错）</summary>
    InvalidGlobPattern,

    /// <summary>当 glob 用的表达式里一个通配符都没有</summary>
    GlobHasNoWildcard,

    /// <summary>搜索引擎抛异常</summary>
    EngineFailed,
}

/// <summary>
/// 一次搜索失败的<b>结构化</b>原因。
///
/// 从前失败是以字符串形式混进结果列表返回的（<c>"[Error] ..."</c>、
/// <c>FileName = "" 的假命中"</c>）。那样有三个后果，这个类型就是为了消掉它们：
/// <list type="number">
/// <item>界面的快速搜索把这些字符串当成了一条搜索结果显示。</item>
/// <item>失败与"搜到了 0 条"分不开——尤其 Grep 的目录不存在从前直接返回空列表，
/// 模型于是以为"这个词确实没有"，换个词再搜，反复几轮都在错的目录里打转。</item>
/// <item>把类型信息编码进字符串再靠前缀解回来，措辞一改另一头就静默失效。</item>
/// </list>
///
/// <b>面向模型的措辞不在这里</b>：这里只登记事实，怎么跟模型说话是工具边界
/// （<c>PermissiveFileAccessTools</c>）的职责，怎么跟用户说话是界面层的职责。
/// 同一份事实，两边各按自己的读者渲染。
/// </summary>
public sealed class SearchFailure
{
    /// <summary>失败种类</summary>
    public required ESearchFailureKind Kind { get; init; }

    /// <summary>调用方原样传进来的目录（可能为 null/空，表示没传）</summary>
    public string? RequestedDirectory { get; init; }

    /// <summary>上一项解析之后的绝对路径。<b>回显它是治"乱编路径"的关键</b>：
    /// 模型看不到自己那个相对路径被拼成了哪里，就只能再猜一次</summary>
    public string ResolvedDirectory { get; init; } = string.Empty;

    /// <summary>搜索器的工作区根目录</summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>出问题的表达式（glob 或正则）</summary>
    public string Pattern { get; init; } = string.Empty;

    /// <summary>引擎给出的原始说明（异常消息等）；没有则为空串</summary>
    public string Detail { get; init; } = string.Empty;
}

/// <summary>
/// 一条 glob 命中。
///
/// <b>不是渲染好的字符串</b>：它有两个读者——模型那侧要 <c>[FILE] path (12.3 KB)</c> 这种
/// 带大小的行，界面那侧只要路径去打开文件。从前是一个字符串走两头，界面靠剥
/// <c>"[FILE] "</c> 前缀取路径，于是模型那侧一改格式（比如追加文件大小）界面就静默坏掉。
/// 渲染各自在自己那一层做。
/// </summary>
/// <param name="Path">相对工作区的路径（工作区之外则为绝对路径）</param>
/// <param name="IsDirectory">是目录还是文件</param>
/// <param name="SizeBytes">文件字节数；目录为 0</param>
public record GlobEntry(string Path, bool IsDirectory, long SizeBytes);

/// <summary>一次 glob 搜索的结果：命中条目与失败原因分开</summary>
public sealed class GlobOutcome
{
    /// <summary>命中条目，失败时为空</summary>
    public IReadOnlyList<GlobEntry> Entries { get; init; } = Array.Empty<GlobEntry>();

    /// <summary>失败原因；成功则为 null</summary>
    public SearchFailure? Failure { get; init; }

    /// <summary>实际搜索的根目录绝对路径</summary>
    public string ResolvedDirectory { get; init; } = string.Empty;

    /// <summary>命中数达到上限被截断</summary>
    public bool Truncated { get; init; }

    /// <summary>成功且有命中</summary>
    public bool HasHits => Failure == null && Entries.Count > 0;
}

/// <summary>一次文本搜索的结果：命中与失败原因分开</summary>
public sealed class GrepOutcome
{
    /// <summary>命中列表，一处命中一条；失败时为空</summary>
    public IReadOnlyList<GrepMatchResult> Matches { get; init; } = Array.Empty<GrepMatchResult>();

    /// <summary>失败原因；成功则为 null</summary>
    public SearchFailure? Failure { get; init; }

    /// <summary>实际搜索的根目录绝对路径</summary>
    public string ResolvedDirectory { get; init; } = string.Empty;

    /// <summary>
    /// 表达式当正则编译不过，已自动降级为字面串搜索。
    ///
    /// 这个标志<b>必须告诉模型</b>：它以为自己传的是正则，实际按字面串搜的，
    /// 不说一声，它对"为什么少了几条命中"会得出错误结论。
    /// </summary>
    public bool FellBackToLiteral { get; init; }

    /// <summary>
    /// 经过 glob 味归一化之后真正拿去搜的表达式。与调用方给的不同时才有意义。
    ///
    /// 实测模型偏爱写 <c>*Foo</c> 这种半 glob 半正则的东西：当正则它是非法的
    /// （<c>Quantifier '*' following nothing</c>），当字面串又搜不到，两条路都不通。
    /// 归一化把裸的前导/尾随 <c>*</c> 补成 <c>.*</c>，这才让它一次命中。
    /// </summary>
    public string EffectiveQuery { get; init; } = string.Empty;
}
