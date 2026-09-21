using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UiharuMind.Localization.Generator.Model;

namespace UiharuMind.Localization.Generator.Parsing;

/// <summary>resx 资源解析器。只关心 <c>&lt;data name="X"&gt;&lt;value&gt;</c> 结构，不依赖任何第三方包。</summary>
internal static class ResxResourceParser
{
    // 文件名末尾的 culture 后缀，如 Lang.zh-hans.resx → zh-hans；Lang.resx → 无匹配（默认文化）
    private static readonly Regex CultureSuffix = new(
        @"\.(?<culture>[a-zA-Z]{2,3}(-[a-zA-Z]{2,4})?)$",
        RegexOptions.CultureInvariant);

    /// <summary>解析结果：要么产出 CultureResourceSet，要么给出错误信息。</summary>
    internal readonly struct ParseResult
    {
        private ParseResult(CultureResourceSet? resource, string? error)
        {
            Resource = resource;
            Error = error;
        }

        internal CultureResourceSet? Resource { get; }

        internal string? Error { get; }

        internal static ParseResult Ok(CultureResourceSet resource) => new(resource, null);

        internal static ParseResult Failed(string error) => new(null, error);
    }

    /// <summary>解析一个 resx 文件内容。</summary>
    internal static ParseResult Parse(string filePath, string content)
    {
        var fileName = Path.GetFileName(filePath);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        string? cultureName = null;

        var match = CultureSuffix.Match(baseName);
        if (match.Success)
        {
            cultureName = match.Groups["culture"].Value;
            baseName = baseName.Substring(0, match.Index);
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(content);
        }
        catch (Exception ex)
        {
            return ParseResult.Failed($"'{fileName}' 不是合法 XML: {ex.Message}");
        }

        var keys = new List<ResourceKeyValue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (document.Root != null && document.Root.Name.LocalName == "root")
        {
            foreach (var data in document.Root.Elements("data"))
            {
                var name = data.Attribute("name")?.Value;
                if (name is null || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var key = name.Trim();
                if (!seen.Add(key))
                {
                    return ParseResult.Failed($"'{fileName}' 中存在重复 key '{key}'");
                }

                var value = data.Element("value")?.Value ?? string.Empty;
                keys.Add(new ResourceKeyValue(key, value));
            }
        }

        if (keys.Count == 0)
        {
            return ParseResult.Failed($"'{fileName}' 没有可解析的 <data> 条目");
        }

        return ParseResult.Ok(new CultureResourceSet(
            displayName: Path.GetFileNameWithoutExtension(fileName),
            cultureName: cultureName,
            isDefaultCulture: cultureName is null,
            sourcePath: filePath,
            keys: keys.ToImmutableArray()));
    }
}