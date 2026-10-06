namespace UiharuMind.Shared.Data;

/// <summary>
/// 设置下拉里的一项
/// </summary>
/// <param name="Value">取值</param>
/// <param name="DisplayName">显示名</param>
public sealed record SettingChoice<T>(T Value, string DisplayName);
