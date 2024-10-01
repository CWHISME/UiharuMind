namespace UiharuMind.Core.Core.Attributes;

/// <summary>
/// 只允许从列表中选择选项
/// </summary>
public class SettingConfigOptionsAttribute : Attribute
{
    public string[] Options { get; private set; }

    public SettingConfigOptionsAttribute(params string[] options)
    {
        Options = options;
    }
}