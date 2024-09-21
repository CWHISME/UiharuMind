namespace UiharuMind.Core.Core.Attributes;

public class SettingConfigDescAttribute : Attribute
{
    public string Description { get; set; }

    public SettingConfigDescAttribute(string description)
    {
        Description = description;
    }
}