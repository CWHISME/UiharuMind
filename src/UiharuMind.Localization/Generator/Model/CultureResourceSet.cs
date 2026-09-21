using System.Collections.Immutable;

namespace UiharuMind.Localization.Generator.Model;

/// <summary>一条语言资源：key（原始字符串）与默认文案。</summary>
internal sealed class ResourceKeyValue
{
    internal ResourceKeyValue(string key, string value)
    {
        Key = key;
        Value = value;
    }

    /// <summary>资源 key，即生成的常量名（identity mapping）。</summary>
    internal string Key { get; }

    /// <summary>默认语言文案，仅用于生成的 XML 注释。</summary>
    internal string Value { get; }
}

/// <summary>
/// 一个文化（culture）下的全部 key 集合。这是格式无关的中立模型：
/// 无论 resx / JSON / XML，解析器最终都收敛成它。
/// </summary>
internal sealed class CultureResourceSet
{
    internal CultureResourceSet(
        string displayName,
        string? cultureName,
        bool isDefaultCulture,
        string sourcePath,
        ImmutableArray<ResourceKeyValue> keys)
    {
        DisplayName = displayName;
        CultureName = cultureName;
        IsDefaultCulture = isDefaultCulture;
        SourcePath = sourcePath;
        Keys = keys;
    }

    /// <summary>文件基名（如 Lang.zh-hans），用于错误信息展示。</summary>
    internal string DisplayName { get; }

    /// <summary>culture 名；null 表示默认文化。</summary>
    internal string? CultureName { get; }

    /// <summary>是否默认文化（文件名不含 culture 后缀）。</summary>
    internal bool IsDefaultCulture { get; }

    /// <summary>源文件路径。</summary>
    internal string SourcePath { get; }

    internal ImmutableArray<ResourceKeyValue> Keys { get; }
}