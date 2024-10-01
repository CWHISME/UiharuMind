namespace UiharuMind.Core.Core.Attributes;

public class SettingConfigRangeAttribute : Attribute
{
    public double MinValue { get; set; }
    public double MaxValue { get; set; }

    public double Step { get; set; }

    public SettingConfigRangeAttribute(double min, double max, double step = 1)
    {
        MinValue = min;
        MaxValue = max;
        Step = step;
    }
}