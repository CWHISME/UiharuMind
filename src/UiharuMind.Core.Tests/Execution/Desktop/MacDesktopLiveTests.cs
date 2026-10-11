using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Desktop;
using UiharuMind.Core.AI.Execution.Tools.Desktop.Mac;

namespace UiharuMind.Core.Tests.Execution.Desktop;

/// <summary>
/// 真机：在这台 Mac 上读窗口、动手。要无障碍授权（授给跑测试的终端），只手动跑（--explicit only）。
/// 文本编辑在后台打开一份临时文件，测完只关这一份
/// </summary>
public class MacDesktopLiveTests
{
    [Fact(Explicit = true)]
    public async Task ListsWindows_AndSnapshotsOne()
    {
        if (!OperatingSystem.IsMacOS()) return;
        DesktopHost host = new(new MacDesktopPlatform());
        Assert.Null(host.Unavailable);

        IReadOnlyList<DesktopWindow> windows = await host.ListWindowsAsync(TestContext.Current.CancellationToken);
        DesktopWindow target = windows.First(window => window.Blocked == null);
        DesktopView view = await host.SnapshotAsync("live", target.Id, TestContext.Current.CancellationToken);
        Assert.NotEmpty(view.Snapshot);
    }

    [Fact(Explicit = true)]
    public async Task TextEdit_FillThroughAccessibility_TypeThroughBackgroundEvents()
    {
        if (!OperatingSystem.IsMacOS()) return;
        CancellationToken token = TestContext.Current.CancellationToken;
        string file = Path.Combine(Path.GetTempPath(), $"desktop-live-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "original", token);
        DesktopHost host = new(new MacDesktopPlatform());
        DesktopWindow? window = null;
        try
        {
            window = Assert.Single(await host.OpenAsync(file, isFile: true, token));
            DesktopView view = await host.SnapshotAsync("live", window.Id, token);
            string textArea = RefOf(view.Snapshot, "- textbox");

            DesktopView filled = await host.ActAsync("live",
                new DesktopActionRequest(window.Id, EDesktopAction.Fill, textArea, "filled by accessibility", false), token);
            Assert.Contains("filled by accessibility", filled.Snapshot);

            // 不给目标：落在有焦点的文本区，经无障碍在光标处插入，不发按键
            DesktopView typed = await host.ActAsync("live",
                new DesktopActionRequest(window.Id, EDesktopAction.Type, string.Empty, "typed 中文 ", false), token);
            Assert.Contains("typed 中文", typed.Snapshot);
            Assert.Contains("filled by accessibility", typed.Snapshot);
        }
        finally
        {
            if (window != null) await CloseWithoutSavingAsync(host, window.Id, token);
            File.Delete(file);
        }
    }

    // 只用无障碍动作收尾：点关闭，弹出存储提示就点「不存储」
    private static async Task CloseWithoutSavingAsync(DesktopHost host, string window, CancellationToken token)
    {
        DesktopView view = await host.SnapshotAsync("live", window, token);
        DesktopView after = await host.ActAsync("live",
            new DesktopActionRequest(window, EDesktopAction.Click, RefOf(view.Snapshot, "button \"close\""), "", false), token);
        string? dontSave = after.Snapshot.Split('\n').FirstOrDefault(line => line.Contains("不存储") || line.Contains("Don’t Save") ||
                                                                       line.Contains("Delete"));
        if (dontSave != null)
        {
            await host.ActAsync("live", new DesktopActionRequest(window, EDesktopAction.Click, RefOf(dontSave, "["), "", false), token);
        }
    }

    private static string RefOf(string snapshot, string text)
    {
        string line = snapshot.Split('\n').First(line => line.Contains(text, StringComparison.Ordinal) && line.Contains("[ref="));
        int start = line.IndexOf("[ref=", StringComparison.Ordinal) + 5;
        return line[start..line.IndexOf(']', start)];
    }
}
