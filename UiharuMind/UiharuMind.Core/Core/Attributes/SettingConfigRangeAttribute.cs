/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

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