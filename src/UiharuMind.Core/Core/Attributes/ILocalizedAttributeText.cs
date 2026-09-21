namespace UiharuMind.Core.Core.Attributes;

/// <summary>
/// 「一种语言写一条」的设置项文案特性。挑哪一条由 <c>AttributeExt</c> 统一决定，
/// 免得显示名与描述各写一份挑选逻辑然后慢慢跑偏。
/// </summary>
public interface ILocalizedAttributeText
{
    /// <summary>文案本身</summary>
    string Text { get; }

    /// <summary>该文案对应的语言</summary>
    string LanguageCode { get; }
}
