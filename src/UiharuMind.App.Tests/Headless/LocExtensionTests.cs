using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Shared.Markup;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// <see cref="LocExtension"/> 改走 <c>IObservable&lt;string&gt;.ToBinding()</c> 后的行为守则：
/// 订阅即推当前值、语言/设置变化实时更新、跨线程推值由 observable 封送回 UI 线程。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class LocExtensionTests
{
    [Fact]
    public void LocBinding_PushesInitialValue_AndUpdatesOnLanguageChange()
    {
        HeadlessUi.Run(() =>
        {
            string original = LocalizationManager.Instance.LanguageCode;
            try
            {
                LocalizationManager.Instance.ApplyLanguage("zh-CN", save: false);
                TextBlock tb = new();
                tb.Bind(TextBlock.TextProperty, (BindingBase)new LocExtension("Send").ProvideValue(null!));

                string expectedZh = LocalizationManager.Instance.GetString("Send");
                Assert.Equal("发送", expectedZh); // 中文文案存在，说明查到了资源
                Assert.Equal(expectedZh, tb.Text);

                LocalizationManager.Instance.ApplyLanguage("en-US", save: false);
                string expectedEn = LocalizationManager.Instance.GetString("Send");
                Assert.NotEqual(expectedZh, expectedEn);
                Assert.Equal(expectedEn, tb.Text);
            }
            finally
            {
                LocalizationManager.Instance.ApplyLanguage(original, save: false);
            }
        });
    }

    [Fact]
    public void LocBinding_WithSettingProperty_ShowsShortcut_AndTracksOnlyItsOwnChange()
    {
        HeadlessUi.Run(() =>
        {
            string originalShortcut = SettingConfig.Current.CaptureScreenShortcut;
            string originalOther = SettingConfig.Current.QuickStartChatShortcut;
            try
            {
                SettingConfig.Current.CaptureScreenShortcut = "Alt+Shift+TEST";
                TextBlock tb = new();
                tb.Bind(TextBlock.TextProperty,
                    (BindingBase)new LocExtension("TrayMenuScreenCapture") { SettingProperty = "CaptureScreenShortcut" }.ProvideValue(null!));

                string expected = $"{LocalizationManager.Instance.GetString("TrayMenuScreenCapture")} (Alt+Shift+TEST)";
                Assert.Equal(expected, tb.Text);

                // 改自己关心的快捷键 → 更新
                SettingConfig.Current.CaptureScreenShortcut = "Alt+Shift+NEW";
                expected = $"{LocalizationManager.Instance.GetString("TrayMenuScreenCapture")} (Alt+Shift+NEW)";
                Assert.Equal(expected, tb.Text);

                // 无关设置变化不触发
                SettingConfig.Current.QuickStartChatShortcut = "Alt+Shift+OTHER";
                Assert.Equal(expected, tb.Text);
            }
            finally
            {
                SettingConfig.Current.CaptureScreenShortcut = originalShortcut;
                SettingConfig.Current.QuickStartChatShortcut = originalOther;
            }
        });
    }

    [Fact]
    public void LocBinding_MultipleTargets_AllUpdate()
    {
        HeadlessUi.Run(() =>
        {
            string original = LocalizationManager.Instance.LanguageCode;
            try
            {
                LocalizationManager.Instance.ApplyLanguage("zh-CN", save: false);
                TextBlock a = new();
                TextBlock b = new();
                a.Bind(TextBlock.TextProperty, (BindingBase)new LocExtension("Send").ProvideValue(null!));
                b.Bind(TextBlock.TextProperty, (BindingBase)new LocExtension("Send").ProvideValue(null!));
                string zh = LocalizationManager.Instance.GetString("Send");
                Assert.Equal(zh, a.Text);
                Assert.Equal(zh, b.Text);

                LocalizationManager.Instance.ApplyLanguage("en-US", save: false);
                string en = LocalizationManager.Instance.GetString("Send");
                Assert.Equal(en, a.Text);
                Assert.Equal(en, b.Text);
            }
            finally
            {
                LocalizationManager.Instance.ApplyLanguage(original, save: false);
            }
        });
    }

    [Fact]
    public void LocBinding_SettingChangeFromBackgroundThread_IsMarshalledToUiThread()
    {
        HeadlessUi.Run(() =>
        {
            string originalShortcut = SettingConfig.Current.CaptureScreenShortcut;
            try
            {
                SettingConfig.Current.CaptureScreenShortcut = "Alt+Shift+BG";
                TextBlock tb = new();
                tb.Bind(TextBlock.TextProperty,
                    (BindingBase)new LocExtension("TrayMenuScreenCapture") { SettingProperty = "CaptureScreenShortcut" }.ProvideValue(null!));

                // 在后台线程改快捷键：Push 会走到 Post 封送，不能直接碰控件
                string expected = $"{LocalizationManager.Instance.GetString("TrayMenuScreenCapture")} (Alt+Shift+BG2)";
                Task.Run(() => SettingConfig.Current.CaptureScreenShortcut = "Alt+Shift+BG2")
                    .GetAwaiter().GetResult();

                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (tb.Text != expected && DateTime.UtcNow < deadline)
                {
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
                    Thread.Sleep(5);
                }

                Assert.Equal(expected, tb.Text);
            }
            finally
            {
                SettingConfig.Current.CaptureScreenShortcut = originalShortcut;
            }
        });
    }
}
