/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Tools.Accessibility;

namespace UiharuMind.Core.AI.Execution.Tools.Desktop;

/// <summary>
/// 桌面宿主的看图（ADR 0084）：无障碍树太空（游戏、画布、自绘界面）时截下窗口认字，
/// 认出的每行字成一个带编号的 <c>ocr-text</c> 节点，点它就是点框的中心；要截图时在图上画出所有编号
/// </summary>
public sealed partial class DesktopHost
{
    private const int ThinTreeLines = 4; //有内容的节点少于这么多就算空树
    private const int PictureChangeReads = 5; //画出来的界面：动作后这么多次截图都没变，就当这一步没改变画面
    private const int PictureSettleReads = 13; //一直在动（轮播、加载圈）最多截这么多次

    // 窗口自带的四个按钮与菜单栏不算内容：空树的窗口也有它们
    private static readonly HashSet<string> WindowButtons = ["close", "minimize", "zoom", "full screen"];

    // 单个字符的行多是图标认错（「•」「¢」「贝」），只留看得出意思的：字母数字与常见的箭头、加号、关闭
    private const string MeaningfulSymbols = "<>‹›«»+×✕?!";

    /// <summary>认字与画框（界面层挂上）；没挂时只读无障碍树</summary>
    public IDesktopVision? Vision { get; set; }

    private async Task<(AxNode Tree, byte[]? Picture)> AddPictureAsync(DesktopWindow window, AxNode tree, EDesktopView view,
        List<string> notes, byte[]? captured, CancellationToken cancellationToken)
    {
        bool thin = IsThin(tree);
        if (view == EDesktopView.Outline && !thin) return (tree, null);
        if (Vision == null)
        {
            if (view != EDesktopView.Outline) throw new AxActionException("Reading the window's picture is not available here.");
            notes.Add("This window shows little through accessibility, and reading its picture is not available here.");
            return (tree, null);
        }

        byte[] picture;
        try
        {
            picture = captured ?? await Task.Run(() => _platform!.CaptureWindow(window.Info), cancellationToken);
        }
        catch (AxActionException e) when (view == EDesktopView.Outline)
        {
            notes.Add("This window shows little through accessibility. " + e.Message);
            return (tree, null);
        }

        if (view == EDesktopView.Screenshot && !thin) return (tree, picture);

        List<DesktopTextLine> lines = (await Vision.RecognizeAsync(picture, cancellationToken)).Where(Meaningful).ToList();
        if (lines.Count == 0)
        {
            notes.Add("No text could be read from the window's picture either.");
            return (tree, picture);
        }

        AxRect bounds = window.Info.Bounds;
        AxNode screenText = new("group", "Text read from the window's picture")
        {
            Children = lines.Select(line => new AxNode(AxRoles.OcrText, line.Text)
            {
                Bounds = ToScreen(line.Box, bounds), Value = Position(line.Box, bounds),
            }).ToList(),
        };
        notes.Add($"{lines.Count} lines were read from the window's picture (ocr-text, each with the x,y of its middle in the window); " +
                  "acting on one clicks its middle.");
        return (tree with { Children = [.. tree.Children, screenText] }, picture);
    }

    /// <summary>
    /// 画出来的界面动作后树不变，改比截图：等画面变了再稳下来（两次一样）；一直没变就等满几次认了
    /// </summary>
    /// <returns>稳下来的那张图；截不了为 null，交给认字那步去说明</returns>
    private async Task<byte[]?> SettlePictureAsync(DesktopWindow window, byte[]? before, CancellationToken cancellationToken)
    {
        byte[]? previous = null;
        try
        {
            for (int read = 1; ; read++)
            {
                await Task.Delay(SettleDelayMs, cancellationToken);
                byte[] now = await Task.Run(() => _platform!.CaptureWindow(window.Info), cancellationToken);
                bool stable = previous != null && now.AsSpan().SequenceEqual(previous);
                bool changed = before == null || !now.AsSpan().SequenceEqual(before);
                if ((stable && (changed || read >= PictureChangeReads)) || read >= PictureSettleReads) return now;
                previous = now;
            }
        }
        catch (AxActionException)
        {
            return null;
        }
    }

    private static bool IsThin(AxNode tree) => Informative(tree) < ThinTreeLines;

    // 编号框只画窗口里看得见的
    private byte[] Mark(byte[] picture, AxSnapshot snapshot, AxRect window)
    {
        List<DesktopMark> marks = new();
        foreach ((string refId, AxNode node) in snapshot.Refs)
        {
            if (node.Bounds is not { IsEmpty: false } box) continue;
            AxRect normalized = new((box.X - window.X) / window.Width, (box.Y - window.Y) / window.Height,
                box.Width / window.Width, box.Height / window.Height);
            if (normalized.X >= 1 || normalized.Y >= 1 || normalized.X + normalized.Width <= 0 || normalized.Y + normalized.Height <= 0) continue;
            marks.Add(new DesktopMark(refId, normalized));
        }

        return Vision!.Mark(picture, marks);
    }

    private static bool Meaningful(DesktopTextLine line)
    {
        string text = line.Text.Trim();
        return text.Length > 1 || (text.Length == 1 && (char.IsAsciiLetterOrDigit(text[0]) || MeaningfulSymbols.Contains(text[0])));
    }

    // 认出的字没有排版，给出中心在窗口里的位置：分得出侧栏与正文、同一行与上下行
    private static string Position(AxRect box, AxRect window) =>
        $"at {Math.Round((box.X + box.Width / 2) * window.Width)},{Math.Round((box.Y + box.Height / 2) * window.Height)}";

    private static AxRect ToScreen(AxRect box, AxRect window) =>
        new(window.X + box.X * window.Width, window.Y + box.Y * window.Height, box.Width * window.Width, box.Height * window.Height);

    private static int Informative(AxNode node) => node.Role == "menubar" || WindowButtons.Contains(node.Name)
        ? 0
        : (node.Name.Trim().Length > 0 || node.Value.Trim().Length > 0 ? 1 : 0) + node.Children.Sum(Informative);
}
