using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using UiharuMind.Localization.Generator.Emit;
using UiharuMind.Localization.Generator.Model;
using UiharuMind.Localization.Generator.Parsing;

namespace UiharuMind.Localization.Generator;

/// <summary>
/// 生成器：把项目里作为 AdditionalFiles 引入的语言资源文件（*.resx）编译期生成
/// <c>LangKey</c> 强类型枚举（identity mapping：成员名即资源 key）；并对 XAML/C#
/// 的消费点做静态校验：
///   LK2001 —— axaml 里 <c>{loc:Loc X}</c> 的 X 不存在于枚举（拼写错误，Error）；
///   LK2002 —— 枚举成员从未被 XAML/C# 引用（死 key 候选，Warning）。
/// 管道是格式无关的——新增 JSON/XML 只需要加一个新的 Parser。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class LangKeySourceGenerator : IIncrementalGenerator
{
    private const string DefaultRootNamespace = "UiharuMind.Generated";
    private const string ClassName = "LangKey";
    private const string RootNamespaceProperty = "build_property:LangKeysRootNamespace";
    private const string CommentCultureProperty = "build_property:LangKeysCommentCulture";
    private const string GeneratedFileMarker = "LangKey.g.cs";

    // XAML: {前缀:Loc Key}（loc:Loc / markup:Loc 等任意前缀）
    private static readonly Regex AxamlKeyPattern = new(
        @"\{[A-Za-z_][A-Za-z0-9_]*:Loc\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:,|\})",
        RegexOptions.CultureInvariant);

    // C# 强类型引用: LangKey.Member
    private static readonly Regex EnumUsagePattern = new(
        @"\bLangKey\.\s*([A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant);

    // C# 字符串字面量用法（遗留/动态入口之外的字面量调用）: GetString("X") / Text("X")
    private static readonly Regex StringKeyUsagePattern = new(
        @"\b(?:GetString|Text)\(\s*""([A-Za-z_][A-Za-z0-9_]*)""",
        RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 根命名空间（项目级 MSBuild 属性，默认 UiharuMind.Generated）
        var rootNamespace = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
        {
            if (options.GlobalOptions.TryGetValue(RootNamespaceProperty, out var configured)
                && !string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            return DefaultRootNamespace;
        });

        // 注释文案语言（项目级 MSBuild 属性 build_property:LangKeysCommentCulture；缺省 null → 用默认文化文案）
        var commentCulture = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
        {
            if (options.GlobalOptions.TryGetValue(CommentCultureProperty, out var configured)
                && !string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            return null;
        });

        // 1) 解析每个 resx 附加文件 → ParseResult
        var parsedResx = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, token) =>
            {
                var text = file.GetText(token);
                return text is null
                    ? ResxResourceParser.ParseResult.Failed($"无法读取 {file.Path}")
                    : ResxResourceParser.Parse(file.Path, text.ToString());
            })
            .Collect();

        // 2) 收集 axaml 文本（用于 XAML key 校验）
        var axamlTexts = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, token) => (Path: file.Path, Text: file.GetText(token)?.ToString() ?? string.Empty))
            .Collect();

        // 5) 全部输入合成一个输出单元（含 Compilation 以便扫 C# 用法）
        var model = rootNamespace
            .Combine(parsedResx)
            .Combine(axamlTexts)
            .Combine(context.CompilationProvider)
            .Combine(commentCulture)
            .Select(static (t, _) => new InputModel(
                t.Left.Left.Left.Left,
                t.Left.Left.Left.Right,
                t.Left.Left.Right,
                t.Left.Right,
                t.Right));

        context.RegisterSourceOutput(model, static (spc, m) => Emit(spc, m));
    }

    private static void Emit(SourceProductionContext context, InputModel model)
    {
        var rootNamespace = model.RootNamespace;
        var files = model.Files;
        var axamlTexts = model.AxamlTexts;
        var compilation = model.Compilation;
        // 解析错误（LK1001）
        foreach (var parsed in files)
        {
            if (parsed.Error is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ResxParseError, Location.None, parsed.Error));
            }
        }

        // 默认文化集（无 culture 后缀的文件），只取第一个
        var defaultSet = files
            .Select(static parsed => parsed.Resource)
            .FirstOrDefault(static set => set is { IsDefaultCulture: true });
        if (defaultSet is null)
        {
            return; // 项目尚未引入默认语言文件，不生成也不做用法校验
        }

        // 卫星文化完整性（LK1003 / LK1004）
        var defaultKeys = defaultSet.Keys
            .Select(static kv => kv.Key)
            .ToImmutableHashSet(StringComparer.Ordinal);
        foreach (var set in files.Select(static parsed => parsed.Resource))
        {
            if (set is null || set.IsDefaultCulture)
            {
                continue;
            }

            var setKeys = set.Keys.Select(static kv => kv.Key).ToImmutableHashSet(StringComparer.Ordinal);
            var missing = defaultKeys.Except(setKeys).OrderBy(static k => k, StringComparer.Ordinal).ToArray();
            var extra = setKeys.Except(defaultKeys).OrderBy(static k => k, StringComparer.Ordinal).ToArray();

            if (missing.Length > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.MissingInCulture,
                    Location.None,
                    set.CultureName ?? set.DisplayName,
                    missing.Length,
                    string.Join(", ", missing.Take(3))));
            }

            if (extra.Length > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.ExtraInCulture,
                    Location.None,
                    set.CultureName ?? set.DisplayName,
                    extra.Length,
                    string.Join(", ", extra.Take(3))));
            }
        }

        // 校验标识符（LK1002）+ 排序
        var validKeys = new List<ResourceKeyValue>();
        foreach (var kv in defaultSet.Keys.OrderBy(static kv => kv.Key, StringComparer.Ordinal))
        {
            if (!SyntaxFacts.IsValidIdentifier(kv.Key))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.InvalidKeyIdentifier,
                    Location.None,
                    $"{kv.Key}（{defaultSet.SourcePath}）"));
                continue;
            }

            validKeys.Add(kv);
        }

        if (validKeys.Count == 0)
        {
            return;
        }

        // 1) 枚举输出（注释文案语言由 MSBuild 属性 LangKeysCommentCulture 指定，缺省用默认文化文案）
        var commentCulture = model.CommentCulture;
        var commentSet = files
            .Select(static parsed => parsed.Resource)
            .FirstOrDefault(set => set is { CultureName: not null }
                && commentCulture is not null
                && set.CultureName.Equals(commentCulture, StringComparison.OrdinalIgnoreCase));
        var emittedKeys = ApplyCommentCulture(validKeys, commentSet);
        context.AddSource(
            $"{ClassName}.g.cs",
            SourceText.From(LangKeyEmitter.Emit(
                rootNamespace,
                ClassName,
                defaultSet.SourcePath,
                emittedKeys), Encoding.UTF8));

        // 2) XAML key 校验（LK2001）与死 key 报告（LK2002）
        ReportUsageDiagnostics(context, defaultKeys, axamlTexts, compilation);
    }

    /// <summary>
    /// 把默认文化 key 列表的文案值替换成注释指定文化的文案（供生成 XML 文档注释用），
    /// 缺 key 时回退默认文案；未指定文化或找不到对应文件时原样返回。
    /// </summary>
    private static List<ResourceKeyValue> ApplyCommentCulture(
        List<ResourceKeyValue> defaultKeys,
        CultureResourceSet? commentSet)
    {
        if (commentSet is null)
        {
            return defaultKeys;
        }

        var values = commentSet.Keys.ToDictionary(
            static kv => kv.Key,
            static kv => kv.Value,
            StringComparer.Ordinal);
        return defaultKeys
            .Select(kv => values.TryGetValue(kv.Key, out var commentValue)
                ? new ResourceKeyValue(kv.Key, commentValue)
                : kv)
            .ToList();
    }

    private static void ReportUsageDiagnostics(
        SourceProductionContext context,
        ImmutableHashSet<string> defaultKeys,
        ImmutableArray<(string Path, string Text)> axamlTexts,
        Compilation compilation)
    {
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);

        // XAML 侧: {loc:Loc X}
        foreach (var (path, text) in axamlTexts)
        {
            foreach (Match match in AxamlKeyPattern.Matches(text))
            {
                var key = match.Groups[1].Value;
                if (!defaultKeys.Contains(key))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.MissingKeyInXaml,
                        CreateLineLocation(path, text, match.Index),
                        key));
                    continue;
                }

                usedKeys.Add(key);
            }
        }

        // C# 侧: LangKey.X 强类型引用 + GetString("X")/Text("X") 字面量（排除生成文件自身）
        foreach (var tree in compilation.SyntaxTrees)
        {
            var path = tree.FilePath;
            // 排除生成文件自身（枚举声明，非消费点）
            if (path.EndsWith(GeneratedFileMarker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = tree.GetText().ToString();
            foreach (Match match in EnumUsagePattern.Matches(text))
            {
                usedKeys.Add(match.Groups[1].Value);
            }

            foreach (Match match in StringKeyUsagePattern.Matches(text))
            {
                usedKeys.Add(match.Groups[1].Value);
            }
        }

        // 死 key（LK2002）—— 可能被 $"前缀{...}" 动态拼接使用，消息里说明
        var deadKeys = defaultKeys
            .Except(usedKeys)
            .OrderBy(static k => k, StringComparer.Ordinal)
            .ToArray();
        if (deadKeys.Length > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UnusedKey,
                Location.None,
                deadKeys.Length,
                string.Join(", ", deadKeys.Take(10)),
                deadKeys.Length > 10 ? $"（还有 {deadKeys.Length - 10} 个）" : string.Empty));
        }
    }

    private static Location CreateLineLocation(string path, string text, int index)
    {
        var line = 0;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        var span = new TextSpan(index, 1);
        var linePos = new LinePosition(line, 0);
        return Location.Create(path, span, new LinePositionSpan(linePos, linePos));
    }

    private sealed class InputModel
    {
        internal InputModel(
            string rootNamespace,
            ImmutableArray<ResxResourceParser.ParseResult> files,
            ImmutableArray<(string Path, string Text)> axamlTexts,
            Compilation compilation,
            string? commentCulture)
        {
            RootNamespace = rootNamespace;
            Files = files;
            AxamlTexts = axamlTexts;
            Compilation = compilation;
            CommentCulture = commentCulture;
        }

        internal string RootNamespace { get; }

        internal ImmutableArray<ResxResourceParser.ParseResult> Files { get; }

        internal ImmutableArray<(string Path, string Text)> AxamlTexts { get; }

        internal Compilation Compilation { get; }

        internal string? CommentCulture { get; }
    }
}