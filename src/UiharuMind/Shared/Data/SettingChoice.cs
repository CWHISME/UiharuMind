namespace UiharuMind.Shared.Data;

/// <summary>
/// 设置下拉里的一项（不带取值类型，给 XAML 的编译绑定当 DataType 用）
/// </summary>
public interface ISettingChoice
{
    /// <summary>显示名</summary>
    string DisplayName { get; }
}

/// <summary>
/// 设置下拉里的一项
/// </summary>
/// <param name="Value">取值</param>
/// <param name="DisplayName">显示名</param>
public sealed record SettingChoice<T>(T Value, string DisplayName) : ISettingChoice;
