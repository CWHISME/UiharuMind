namespace UiharuMind.Core.Core.Attributes;

/// <summary>
/// 可以从列表选择，同时也允许手动填写
/// </summary>
public class SettingConfigCustomOptionsAttribute : Attribute
{
    public string[] Options { get; set; }

    public SettingConfigCustomOptionsAttribute(params string[] options)
    {
        Options = options;
    }
}