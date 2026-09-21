/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.IO;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Shared.Services.TextMate;

/// <summary>
/// LongTextView 的语法高亮协调器：把「高亮」从控件里整个抽出来，控件只负责调用
/// <see cref="Apply"/> / <see cref="UpdateTheme"/>。
///
/// 职责分三层，各管一段：
/// <list type="number">
/// <item><b>本类</b>：语言解析（扩展名 → scope）、<see cref="IGrammar"/> 与
/// <see cref="Theme"/> 的挂载时机、文档变更监听；语法表本身共用
/// <see cref="TextMateRegistryPool"/> 的那一份；</item>
/// <item><see cref="TextMatePreTokenizer"/>：分帧预 tokenize + 增量失效；</item>
/// <item><see cref="TextMateColorMapTransformer"/>：渲染时只查表上色。</item>
/// </list>
///
/// 为什么不用 AvaloniaEdit.TextMate 官方集成：它在渲染每个可视行时同步 tokenize，
/// 拖选长行越过右边缘会触发上游 issue #606 的无限事件循环，tokenize 成本叠进循环里
/// 把 UI 线程填满冻死。这里把成本挪出循环，渲染路径只剩查表。
/// </summary>
public sealed class TextMateSyntaxService : IDisposable
{
    /// <summary>超过这个字符数一律不上高亮：全文窗的立身之本是"几十万字秒开"，不能为配色让路</summary>
    private const int MaxHighlightChars = 256 * 1024;

    private readonly TextEditor _editor;
    private readonly TextMateColorMapTransformer _transformer;
    private TextMatePreTokenizer? _tokenizer;
    private TextDocument? _highlightedDocument;
    private IGrammar? _grammar;
    private string? _sourceName;
    private bool _installed;
    private ThemeName _appliedTheme; //本实例的着色是按哪个主题挂的,与共享注册表的当前主题比对

    public TextMateSyntaxService(TextEditor editor)
    {
        _editor = editor;
        _transformer = new TextMateColorMapTransformer();
    }

    /// <summary>换源/换文档/换语法时调用。<paramref name="sourceName"/> 为文件名或扩展名，null 表示不上色</summary>
    public void Apply(string? sourceName, string? text)
    {
        _sourceName = sourceName;

        string? scope = ResolveScope(sourceName, text);
        if (scope == null)
        {
            Uninstall();
            return;
        }

        // 文档换了（ApplyText 每次都 new TextDocument）：tokenize 状态整体重来
        if (_grammar == null || !ReferenceEquals(_highlightedDocument, _editor.Document))
        {
            Install(scope);
            return;
        }

        // 同一文档换语法（罕见）：只换 grammar 与主题，重 tokenize
        Registry registry = TextMateRegistryPool.Registry;
        _grammar = registry.LoadGrammar(scope);
        _appliedTheme = TextMateRegistryPool.CurrentTheme;
        _tokenizer!.Start(_editor.Document, _grammar, registry.GetTheme());
    }

    /// <summary>应用主题切换：保留已 tokenize 的 scopes，只重匹配颜色并重绘</summary>
    public void UpdateTheme()
    {
        if (_grammar == null) return;

        ThemeName theme = TextMateRegistryPool.CurrentTheme;
        if (theme == _appliedTheme) return;

        _appliedTheme = theme;
        // 取 Registry 这一下就已经把新主题挂到共享注册表上了，这里只负责把自己的着色重挂
        _tokenizer?.SetTheme(TextMateRegistryPool.Registry.GetTheme());
    }

    /// <summary>从编辑器渲染管线摘掉并释放。窗口隐藏（缓存复用）时调用，避免空窗白挂成本</summary>
    public void Uninstall()
    {
        if (_installed)
        {
            var transformers = _editor.TextArea?.TextView?.LineTransformers;
            if (transformers != null)
            {
                for (int i = transformers.Count - 1; i >= 0; i--)
                {
                    if (transformers[i] is TextMateColorMapTransformer)
                    {
                        transformers.RemoveAt(i);
                        break;
                    }
                }
            }
            _installed = false;
        }

        if (_tokenizer != null)
        {
            _tokenizer.Dispose();
            _tokenizer = null;
        }

        // 退订的是当初订阅的那份文档，不是“当前文档”：换源时 Editor.Document 已是新的，
        // 拿它退订会让旧文档的事件还挂着本对象（泄漏）
        if (_highlightedDocument != null)
        {
            _highlightedDocument.Changed -= OnDocumentChanged;
            _highlightedDocument = null;
        }
        _grammar = null;
    }

    public void Dispose()
    {
        Uninstall();
    }

    /// <summary>可编辑档的文档变化（含流式 AppendText）：从变更行往后增量重跑</summary>
    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_tokenizer == null || _editor.Document == null) return;

        int line0 = _editor.Document.GetLineByOffset(Math.Min(e.Offset, _editor.Document.TextLength)).LineNumber - 1;
        _tokenizer.InvalidateFrom(line0);
    }

    private void Install(string scope)
    {
        Uninstall(); // 摘旧装新：换文档时 transformer/订阅都要重挂

        Registry registry = TextMateRegistryPool.Registry;
        _grammar = registry.LoadGrammar(scope);
        _appliedTheme = TextMateRegistryPool.CurrentTheme;
        _highlightedDocument = _editor.Document;

        var transformers = _editor.TextArea?.TextView?.LineTransformers;
        if (transformers == null || _editor.Document == null) return;

        transformers.Add(_transformer);
        _installed = true;
        _editor.Document.Changed += OnDocumentChanged;

        _tokenizer = new TextMatePreTokenizer(_editor.TextArea!.TextView, _transformer);
        _tokenizer.Start(_editor.Document, _grammar, registry.GetTheme());
    }

    private string? ResolveScope(string? sourceName, string? text)
    {
        if (string.IsNullOrEmpty(sourceName)) return null;
        if ((text?.Length ?? 0) > MaxHighlightChars) return null;

        string extension = Path.GetExtension(sourceName);
        if (string.IsNullOrEmpty(extension)) return null;

        RegistryOptions options = TextMateRegistryPool.Options;
        Language? language = options.GetLanguageByExtension(extension);
        return language == null ? null : options.GetScopeByLanguageId(language.Id);
    }
}
