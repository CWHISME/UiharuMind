using System.ComponentModel;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.Tests.Configs;

/// <summary>
/// 文本窗偏好：行号按代码 / 正文各一份，开关改了要通知（别的文本窗靠它跟着变），没变就不吵
/// </summary>
public class TextFileSettingConfigTests
{
    [Fact]
    public void Defaults_ShowLineNumbersForCodeButNotForProse()
    {
        TextFileSettingConfig config = new();

        Assert.True(config.CodeLineNumbers);
        Assert.False(config.TextLineNumbers);
        Assert.True(config.WordWrap);
        Assert.True(config.SyntaxHighlight);
    }

    [Fact]
    public void Toggles_NotifyOnlyWhenTheValueChanges()
    {
        TextFileSettingConfig config = new();
        List<string?> changed = new();
        config.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        config.WordWrap = false;
        config.WordWrap = false;
        config.TextLineNumbers = true;

        Assert.Equal([nameof(TextFileSettingConfig.WordWrap), nameof(TextFileSettingConfig.TextLineNumbers)], changed);
    }
}
