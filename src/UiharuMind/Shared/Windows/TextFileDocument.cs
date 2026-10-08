using System;
using System.IO;
using System.Linq;
using System.Text;

namespace UiharuMind.Shared.Windows;

/// <summary>
/// 文本窗里的那一份文档：从哪来、能不能写回、按什么编码存、改没改。
/// 窗口只管显示与交互；这份文档叫什么、在哪、按什么语言高亮、算不算正文，都问它。
///
/// 三种来源：磁盘文件（可编辑）、空文档、只读文本（工具结果、旁白全文、超编辑上限的文件、流式的思考过程）
/// </summary>
internal sealed class TextFileDocument
{
    private static readonly string[] ProseExtensions = [".md", ".markdown", ".txt"]; //行号偏好另记一份的正文类
    private static readonly string[] MarkdownExtensions = [".md", ".markdown"];

    private TextFileDocument(string? filePath, string? sourceName, string? displayName, bool isReadOnly,
        Encoding encoding, bool hasBom)
    {
        FilePath = filePath;
        SourceName = sourceName;
        DisplayName = displayName;
        IsReadOnly = isReadOnly;
        Encoding = encoding;
        HasBom = hasBom;
    }

    /// <summary>
    /// 磁盘上的文件，可编辑、能写回原路径
    /// </summary>
    /// <param name="filePath">文件绝对路径</param>
    /// <param name="encoding">读入时探测出的编码</param>
    /// <param name="hasBom">原文件是否带 BOM</param>
    /// <returns>文档</returns>
    public static TextFileDocument ForFile(string filePath, Encoding encoding, bool hasBom) =>
        new(filePath, null, null, false, encoding, hasBom);

    /// <summary>
    /// 空文档：没有路径，保存即另存为
    /// </summary>
    /// <returns>文档</returns>
    public static TextFileDocument Untitled() => new(null, null, null, false, Encoding.UTF8, false);

    /// <summary>
    /// 只读文本，没有回写路径
    /// </summary>
    /// <param name="title">标题（如「Read · 结果」）</param>
    /// <param name="sourceName">来源文件名或路径：决定高亮、行号与默认预览；为空不高亮</param>
    /// <returns>文档</returns>
    public static TextFileDocument ReadOnly(string title, string? sourceName) =>
        new(null, sourceName, title, true, Encoding.UTF8, false);

    /// <summary>回写路径；空文档与只读文本为 null</summary>
    public string? FilePath { get; private set; }

    /// <summary>只读文本的来源（读的那个文件、超限的那个文件）</summary>
    public string? SourceName { get; }

    /// <summary>只读文本的标题</summary>
    public string? DisplayName { get; }

    /// <summary>只读：不可编辑，保存只能导出副本</summary>
    public bool IsReadOnly { get; }

    /// <summary>落盘编码</summary>
    public Encoding Encoding { get; private set; }

    /// <summary>落盘时是否带 BOM</summary>
    public bool HasBom { get; private set; }

    /// <summary>有没有未保存的改动</summary>
    public bool IsDirty { get; set; }

    /// <summary>语言来源：高亮、行号分类与默认预览都按它的扩展名。文件是它本身，只读文本是它的来源</summary>
    public string? LanguageSource => FilePath ?? SourceName;

    /// <summary>正文类（Markdown、纯文本、没有来源）：行号偏好与代码分开记</summary>
    public bool IsProse => LanguageSource == null || HasExtension(LanguageSource, ProseExtensions);

    /// <summary>来源是 Markdown 文件：打开默认进预览</summary>
    public bool IsMarkdown => LanguageSource != null && IsMarkdownPath(LanguageSource);

    /// <summary>预览里相对链接的解析基目录；来源是相对路径时无从解析，为 null</summary>
    public string? LinkBaseDirectory =>
        LanguageSource != null && Path.IsPathFullyQualified(LanguageSource) ? Path.GetDirectoryName(LanguageSource) : null;

    /// <summary>编码名按惯例显示（utf-8 → UTF-8、gb18030 → GB18030），带 BOM 的标出来</summary>
    public string EncodingDisplay
    {
        get
        {
            string display = Encoding.WebName switch
            {
                "utf-8" => "UTF-8",
                "utf-16" => "UTF-16",
                "utf-32" => "UTF-32",
                "gb18030" => "GB18030",
                "big5" => "Big5",
                "shift_jis" => "Shift_JIS",
                _ => Encoding.WebName,
            };
            return HasBom ? $"{display} (BOM)" : display;
        }
    }

    /// <summary>
    /// 路径是不是 Markdown 文件
    /// </summary>
    /// <param name="path">路径或文件名</param>
    /// <returns>是 .md / .markdown 为 true</returns>
    public static bool IsMarkdownPath(string path) => HasExtension(path, MarkdownExtensions);

    /// <summary>
    /// 标题：只读文本用给定的名字，文件用文件名，空文档「未命名」
    /// </summary>
    /// <param name="untitled">「未命名」的文案</param>
    /// <returns>标题</returns>
    public string GetTitle(string untitled) => DisplayName ?? (FilePath == null ? untitled : Path.GetFileName(FilePath));

    /// <summary>
    /// 状态栏左边那一项：文件路径，只读文本是它的来源
    /// </summary>
    /// <param name="untitled">「未命名」的文案</param>
    /// <returns>显示的位置</returns>
    public string GetLocation(string untitled) => FilePath ?? SourceName ?? DisplayName ?? untitled;

    /// <summary>
    /// 另存为的默认文件名
    /// </summary>
    /// <param name="previewing">正在看 Markdown 预览：没有来源时默认存成 .md</param>
    /// <returns>文件名</returns>
    public string GetDefaultSaveName(bool previewing) =>
        FilePath != null ? Path.GetFileName(FilePath)
        : SourceName != null ? Path.GetFileName(SourceName)
        : previewing ? "untitled.md" : "untitled.txt";

    /// <summary>换成无 BOM 的 UTF-8（原编码存不下正文时）</summary>
    public void SwitchToUtf8()
    {
        Encoding = Encoding.UTF8;
        HasBom = false;
    }

    /// <summary>
    /// 另存为落定：从此对着新路径，改动清零
    /// </summary>
    /// <param name="filePath">新路径</param>
    public void MoveTo(string filePath)
    {
        FilePath = filePath;
        IsDirty = false;
    }

    private static bool HasExtension(string path, string[] extensions) =>
        extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
