using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.Core.Configs;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Generated;
using UiharuMind.Shared.Controls;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Windows;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 文本窗：行号按「代码 / 正文」各记一份偏好、开关改了别的窗口跟着变、「更多」展开时重建最近打开，
/// 以及只读查看（从前的全文窗）——硬切过的副本不进可编辑档，行数按原文算
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class TextFileWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-textfile-").FullName;
    private readonly List<TextFileWindow> _windows = new();
    private readonly TextFileSettingConfig _setting = TextFileSettingConfig.Current;
    private bool _codeLineNumbers;
    private bool _textLineNumbers;

    // 偏好是全局单例，改它会通知所有还开着的文本窗（缓存窗关了只是藏起来），只能在界面线程上改
    public TextFileWindowTests()
    {
        HeadlessUi.Run(() =>
        {
            _codeLineNumbers = _setting.CodeLineNumbers;
            _textLineNumbers = _setting.TextLineNumbers;
            _setting.CodeLineNumbers = true;
            _setting.TextLineNumbers = false;
        });
    }

    public void Dispose()
    {
        HeadlessUi.Run(() =>
        {
            foreach (TextFileWindow window in _windows) window.Close();
            _setting.CodeLineNumbers = _codeLineNumbers;
            _setting.TextLineNumbers = _textLineNumbers;
        });
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言
        }
    }

    private TextFileWindow NewWindow()
    {
        TextFileWindow window = new();
        _windows.Add(window);
        window.Show();
        return window;
    }

    private async Task<TextFileWindow> OpenFile(string name, string text)
    {
        string path = Path.Combine(_dir, name);
        await File.WriteAllTextAsync(path, text);
        TextFileWindow window = NewWindow();
        await window.LoadTextAsync(path, text, new UTF8Encoding(false), false);
        return window;
    }

    private static TextDocumentView View(TextFileWindow window) => window.FindControl<TextDocumentView>("DocumentView")!;

    private static LongTextView Editor(TextFileWindow window) => View(window).TextView;

    private static MenuFlyout MoreMenu(TextFileWindow window, out ThemedIconButton button)
    {
        button = window.GetVisualDescendants().OfType<ThemedIconButton>().Single(x => x.IconName == "ellipsis");
        return (MenuFlyout)button.Flyout!;
    }

    private static MenuItem MenuItemNamed(TextFileWindow window, string name) =>
        MoreMenu(window, out _).Items.OfType<MenuItem>().Single(x => x.Name == name);

    [Fact]
    public void LineNumbers_AreOnForCode_AndOffForProse() => HeadlessUi.RunAsync(async () =>
    {
        TextFileWindow code = await OpenFile("a.cs", "class A {}\n");
        TextFileWindow prose = await OpenFile("a.md", "# 标题\n");

        Assert.True(Editor(code).ShowLineNumbers);
        Assert.False(Editor(prose).ShowLineNumbers);
    });

    /// <summary>开关从前是自动属性：改了不通知，别的窗口不知道，也只碰巧随「最近打开」落盘</summary>
    [Fact]
    public void TogglingLineNumbersInProse_ChangesOnlyProse_AndOtherWindowsFollow() => HeadlessUi.RunAsync(async () =>
    {
        TextFileWindow code = await OpenFile("b.cs", "class B {}\n");
        TextFileWindow first = await OpenFile("b.md", "# 一\n");
        TextFileWindow second = await OpenFile("c.md", "# 二\n");

        MenuItem toggle = MenuItemNamed(first, "LineNumbersMenuItem");
        toggle.IsChecked = true;
        toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.True(_setting.TextLineNumbers);
        Assert.True(_setting.CodeLineNumbers);
        Assert.True(Editor(second).ShowLineNumbers);
        Assert.True(MenuItemNamed(second, "LineNumbersMenuItem").IsChecked);
        Assert.True(Editor(code).ShowLineNumbers);
    });

    [Fact]
    public void OpeningTheMoreMenu_RebuildsRecentFiles() => HeadlessUi.RunAsync(async () =>
    {
        TextFileWindow window = await OpenFile("recent.txt", "hello\n");
        MenuFlyout menu = MoreMenu(window, out ThemedIconButton button);

        menu.ShowAt(button);
        Dispatcher.UIThread.RunJobs();

        MenuItem recent = menu.Items.OfType<MenuItem>().Single(x => x.Name == "RecentMenuItem");
        Assert.Contains(recent.Items.OfType<MenuItem>(), x => x.Header as string == "recent.txt");
        menu.Hide();
    });

    [Fact]
    public void ReadOnlyMarkdownResult_OpensInPreview_AndCannotBeSavedInPlace() => HeadlessUi.Run(() =>
    {
        TextFileWindow window = NewWindow();

        window.SetTextSource("Read · 结果", "# 标题\n正文\n", "docs/readme.md", null);

        Assert.False(Editor(window).IsEditable);
        Assert.True(View(window).IsPreview);
        Assert.True(window.FindControl<ThemedIconToggleButton>("PreviewButton")!.IsChecked);
        Assert.Equal("Read · 结果", window.Title);
        Assert.False(MenuItemNamed(window, "SaveMenuItem").IsEnabled);
    });

    /// <summary>参数是「键: 值」清单不是文档：即使涉及 Markdown 文件也不默认预览</summary>
    [Fact]
    public void ArgumentsOfAMarkdownWrite_StayAsText() => HeadlessUi.Run(() =>
    {
        TextFileWindow window = NewWindow();

        window.SetTextSource("Write · 参数", "filePath: docs/a.md\ncontent: # 标题", "docs/a.md", preview: false);

        Assert.False(View(window).IsPreview);
        Assert.Equal("docs/a.md", Editor(window).SyntaxSourceName);
    });

    /// <summary>只读档喂给编辑器的是硬切过的副本：行数按原文算，状态栏也不报内存文本没有的编码</summary>
    [Fact]
    public void ReadOnlyText_CountsLinesOfTheOriginal() => HeadlessUi.Run(() =>
    {
        TextFileWindow window = NewWindow();

        window.SetTextSource("Shell · 结果", new string('x', 5000), null, null);

        string status = window.FindControl<TextBlock>("StatusTextBlock")!.Text!;
        Assert.StartsWith($"1 {Loc.Text(LangKey.TextFileStatusLines)}", status);
        Assert.DoesNotContain("UTF-8", status);
    });

    [Fact]
    public void LoadingAFileAfterReadOnlyText_MakesTheEditorEditableAgain() => HeadlessUi.RunAsync(async () =>
    {
        TextFileWindow window = NewWindow();
        window.SetTextSource("Shell · 结果", "output", null, null);
        string path = Path.Combine(_dir, "after.cs");
        await File.WriteAllTextAsync(path, "class After {}\n");

        await window.LoadTextAsync(path, "class After {}\n", new UTF8Encoding(false), false);

        Assert.True(Editor(window).IsEditable);
        Assert.Equal("after.cs", window.Title);
        Assert.Equal("class After {}\n", Editor(window).Text);
    });

    /// <summary>
    /// 流式的思考过程：默认按 Markdown 渲染，增量两边一起长，原文 Text 也跟着长（复制、另存为读的是它）；
    /// 换去看别的就退订，源条目不会被窗口钉住
    /// </summary>
    [Fact]
    public void LiveText_GrowsInBothViews_AndIsReleasedWhenTheWindowMovesOn() => HeadlessUi.Run(() =>
    {
        FakeLiveText live = new("**先想清楚**\n\n");
        TextFileWindow window = NewWindow();

        window.SetLiveSource("Thinking", live);
        live.Push("- 第一步");

        Assert.True(View(window).IsPreview);
        Assert.Equal("**先想清楚**\n\n- 第一步", Editor(window).Text);
        Assert.Equal(Editor(window).Text, View(window).FindControl<ChunkedMarkdownViewer>("MarkdownViewer")!.MarkdownText);
        Assert.False(MenuItemNamed(window, "SaveMenuItem").IsEnabled); //流式追加不算改动

        window.SetTextSource("Read · 结果", "other", null, null);

        Assert.Equal(0, live.SubscriberCount);
    });

    [Fact]
    public void ThinkingItem_IsReadLive() => HeadlessUi.Run(() =>
    {
        ThinkingItem item = new() { IsExpanded = true };
        item.Append("**先想清楚**\n\n- 第一步");
        TextFileWindow window = NewWindow();

        window.SetLiveSource("Thinking", item);

        Assert.Equal("**先想清楚**\n\n- 第一步", Editor(window).Text);
        Assert.True(View(window).IsPreview);
    });

    /// <summary>搜索在原文里做：预览态下点搜索先切回原文，工具栏的预览开关跟着灭</summary>
    [Fact]
    public void SearchFromPreview_SwitchesBackToTheRawText() => HeadlessUi.Run(() =>
    {
        TextFileWindow window = NewWindow();
        window.SetTextSource("Read · 结果", "# 标题", "a.md", null);

        View(window).OpenSearch();

        Assert.False(View(window).IsPreview);
        Assert.False(window.FindControl<ThemedIconToggleButton>("PreviewButton")!.IsChecked);
    });

    /// <summary>智能体改了正开着、没动过的文件：原地重新载入，不算改动、不出提示</summary>
    [Fact]
    public void ExternalChange_ReloadsACleanDocumentInPlace() => HeadlessUi.RunAsync(async () =>
    {
        TextFileWindow window = await OpenFile("clean.cs", "class A {}\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "clean.cs"), "class A { int b; }\n");

        await window.CheckDiskChangeAsync();

        Assert.Equal("class A { int b; }\n", Editor(window).Text);
        Assert.False(MenuItemNamed(window, "SaveMenuItem").IsEnabled);
        Assert.False(window.FindControl<Border>("DiskChangeBar")!.IsVisible);
    });

    /// <summary>有未保存改动时不替人做主：留着编辑器里的内容，出提示让人选</summary>
    [Fact]
    public void ExternalChange_OnADirtyDocument_AsksInsteadOfReloading() => HeadlessUi.RunAsync(async () =>
    {
        TextFileWindow window = await OpenFile("dirty.cs", "class A {}\n");
        Editor(window).Editor.Document.Insert(0, "// mine\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "dirty.cs"), "class A { int theirs; }\n");

        await window.CheckDiskChangeAsync();

        Assert.StartsWith("// mine", Editor(window).Text);
        Assert.True(window.FindControl<Border>("DiskChangeBar")!.IsVisible);
        Assert.True(window.FindControl<Button>("ReloadFromDiskButton")!.IsVisible);
    });

    [Fact]
    public void FileDeletedOnDisk_KeepsTheTextAndSaysSo() => HeadlessUi.RunAsync(async () =>
    {
        TextFileWindow window = await OpenFile("gone.cs", "class A {}\n");
        File.Delete(Path.Combine(_dir, "gone.cs"));

        await window.CheckDiskChangeAsync();

        Assert.Equal("class A {}\n", Editor(window).Text);
        Assert.True(window.FindControl<Border>("DiskChangeBar")!.IsVisible);
        Assert.False(window.FindControl<Button>("ReloadFromDiskButton")!.IsVisible);
    });

    [Fact]
    public void StatusBar_ShowsTheFilesLineEnding() => HeadlessUi.RunAsync(async () =>
    {
        TextFileWindow crlf = await OpenFile("crlf.cs", "class A\r\n{\r\n}\r\n");
        TextFileWindow lf = await OpenFile("lf.cs", "class A\n{\n}\n");

        Assert.Contains(" · CRLF · ", crlf.FindControl<TextBlock>("StatusTextBlock")!.Text);
        Assert.Contains(" · LF · ", lf.FindControl<TextBlock>("StatusTextBlock")!.Text);
    });

    /// <summary>跳到行后光标行列跟着变；只读文本的编辑器里是硬切过的副本，行号对不上原文，不显示</summary>
    [Fact]
    public void GoToLine_MovesTheCaret_AndTheReadoutFollows() => HeadlessUi.RunAsync(async () =>
    {
        TextFileWindow window = await OpenFile("goto.cs", "a\nb\nc\nd\n");
        Dispatcher.UIThread.RunJobs();

        View(window).GoToLine(3);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, Editor(window).Editor.TextArea.Caret.Line);
        Assert.Equal(Loc.Text(LangKey.TextFileStatusCaretFormat, 3, 1),
            window.FindControl<TextBlock>("CaretTextBlock")!.Text);

        window.SetTextSource("Shell · 结果", "output", null, null);
        Assert.False(window.FindControl<Button>("CaretButton")!.IsVisible);
    });

    /// <summary>选中文字时行列后面跟上选了多少字符、跨几行</summary>
    [Fact]
    public void Selection_IsCountedInTheCaretReadout() => HeadlessUi.RunAsync(async () =>
    {
        TextFileWindow window = await OpenFile("select.cs", "abc\ndef\nghi\n");
        AvaloniaEdit.TextEditor editor = Editor(window).Editor;

        editor.Select(1, 6); //"bc\ndef"

        string readout = window.FindControl<TextBlock>("CaretTextBlock")!.Text!;
        Assert.EndsWith(Loc.Text(LangKey.TextFileStatusSelectionFormat, 6, 2), readout);

        // 往上拖：起点在后、终点在前
        editor.TextArea.Selection = AvaloniaEdit.Editing.Selection.Create(editor.TextArea, 7, 1);
        Assert.EndsWith(Loc.Text(LangKey.TextFileStatusSelectionFormat, 6, 2),
            window.FindControl<TextBlock>("CaretTextBlock")!.Text);

        editor.Select(0, 0);
        Assert.Equal(Loc.Text(LangKey.TextFileStatusCaretFormat, editor.TextArea.Caret.Line, editor.TextArea.Caret.Column),
            window.FindControl<TextBlock>("CaretTextBlock")!.Text);
    });

    private sealed class FakeLiveText(string initial) : ILiveText
    {
        private readonly List<Action<string>> _subscribers = new();
        private string _text = initial;

        public int SubscriberCount => _subscribers.Count;

        public string Subscribe(Action<string> onAppend)
        {
            _subscribers.Add(onAppend);
            return _text;
        }

        public void Unsubscribe(Action<string> onAppend) => _subscribers.Remove(onAppend);

        public void Push(string delta)
        {
            _text += delta;
            foreach (Action<string> subscriber in _subscribers.ToList()) subscriber(delta);
        }
    }
}
