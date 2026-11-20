using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace UiharuMind.App.Tests.Architecture;

/// <summary>
/// 会话条目集合的写入口护栏：往 <c>Items</c> 里加条目的地方必须走
/// <see cref="UiharuMind.Features.Conversation.ConversationItemSink"/>。
///
/// <para>
/// 为什么需要机械检查：从前这个集合有十几条写入口，判重闸只装在历史补画那一侧，
/// 实时流那一侧各自靠 <c>CallId</c> 或引用自判。于是「同一条消息画两遍」的往复机制是——
/// 只要判重口径有一处不齐，每个消息边界都会补出重复条目（真实日志里 5 小时 298 次对账）。
/// 收口之后新增一条绕过 sink 的写入口，就等于把这个机制重新装回去。
/// </para>
/// <para>
/// 白名单里的每一处都要写清理由：它们要么是 sink 自己的实现，要么是纯 UI 占位
/// （不对应任何历史消息，重复与否由产生方负责）。
/// </para>
/// </summary>
public class ConversationItemWriteBoundaryTests
{
    /// <summary>
    /// 允许直接写集合的<b>文件</b>（相对 App 项目源码根），值是理由。
    /// 会话流自己的文件不给整文件豁免：合法写点必须就地写 <c>// sink-exempt: 理由</c>，
    /// 否则文件里任何角落的新写入口都会被放行（Transcript 曾因此漏掉一处 Insert）。
    /// </summary>
    private static readonly Dictionary<string, string> Exemptions = new(StringComparer.Ordinal)
    {
        ["Features/Conversation/ConversationItemSink.cs"] = "sink 自己的实现",
        ["Features/Conversation/HandoffWritingPlaceholder.cs"] = "纯 UI 占位卡，不对应历史消息",
        ["Features/Conversation/QuickChat/QuickChatResultWindow.axaml.cs"] = "快捷窗口自己的条目集合，不是会话流",
        ["Features/Conversation/DevAutomation/SessionInjectCommand.cs"] = "开发脚本注入，与用户路径无关",
    };

    /// <summary>
    /// 直接写会话条目集合的形态。
    ///
    /// <para>
    /// <c>Discard*</c> 必须一起拦：摘掉条目却不销身份表，下一次同身份的条目会被 sink.Add 静默吞掉
    /// （Retry 后用户气泡消失就是这条路径）。<c>Items</c> 是视图模型暴露的属性名，
    /// 历史上写点最多的就是它，只盯私有字段名等于没拦。
    /// </para>
    /// </summary>
    public static readonly Regex DirectWrite = new(
        @"\b(?:_items|_target|Items)\s*\.\s*(?:Add|Insert|Remove|RemoveAt|Clear|Discard|DiscardRange|DiscardAll)\s*\(",
        RegexOptions.Compiled);

    /// <summary>行级豁免标记：允许直接写的那几处必须就地写清理由</summary>
    private const string ExemptMarker = "sink-exempt:";

    /// <summary>会话流的源码目录（其它页面的 <c>Items</c> 是各自的数据集合，与这条流无关）</summary>
    private const string ConversationRoot = "Features/Conversation";

    /// <summary>
    /// <c>Items</c> 是通用属性名：侧栏、插话队列、缩略图条都有各自的 Items。
    /// 只有提到会话条目类型/写入口的文件才可能是这条流的写点——按类型而不是按目录名过滤，
    /// 既挡得住 <c>ConversationViewModel.Items.Add</c>，又不会误报那些不相干的集合。
    /// </summary>
    private static readonly string[] ConversationItemMarkers =
    [
        "ConversationItemBase", "ConversationItemSink", "ConversationViewModel", "ItemSink",
    ];

    /// <summary>护栏本身的判别力：扩展后的正则要认得 Discard* 与 Items 属性写法</summary>
    [Fact]
    public void DirectWrite_RecognizesDiscardAndItemsProperty()
    {
        Assert.True(DirectWrite.IsMatch("_items.Discard(targets);"));
        Assert.True(DirectWrite.IsMatch("_items.DiscardRange(from, count);"));
        Assert.True(DirectWrite.IsMatch("_sink.Items.Clear();"));
        Assert.True(DirectWrite.IsMatch("Items.Add(item);"));
        Assert.True(DirectWrite.IsMatch("ConversationViewModel.Items.Insert(0, item);"));
        Assert.False(DirectWrite.IsMatch("_sink.Add(item);"));
        Assert.False(DirectWrite.IsMatch("Items.Count"));
    }

    [Fact]
    public void ConversationItems_AreOnlyWrittenThroughTheSink()
    {
        string root = AppSourceRoot();
        string conversation = Path.Combine(root, ConversationRoot.Replace('/', Path.DirectorySeparatorChar));
        List<string> violations = [];
        foreach (string file in Directory.EnumerateFiles(conversation, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("obj/") || relative.StartsWith("bin/")) continue;
            if (Exemptions.ContainsKey(relative)) continue;

            string source = File.ReadAllText(file);
            if (!ConversationItemMarkers.Any(marker => source.Contains(marker, StringComparison.Ordinal))) continue;

            string[] lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("//")) continue;
                if (!DirectWrite.IsMatch(line)) continue;
                if (line.Contains(ExemptMarker, StringComparison.Ordinal)) continue;
                violations.Add($"{relative}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "这些地方直接写了会话条目集合，改为走 ConversationItemSink（判重按身份，不按引用或文本）：\n"
            + string.Join("\n", violations));
    }

    // 本文件在 src/UiharuMind.App.Tests/Architecture/ 下，App 项目源码在 src/UiharuMind/
    private static string AppSourceRoot([CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "UiharuMind"));
        Assert.True(File.Exists(Path.Combine(root, "UiharuMind.csproj")), $"找不到 App 项目源码：{root}");
        return root;
    }
}
