using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace UiharuMind.Features.Conversation.Composer;

/// <summary>
/// 输入框的一种补全（<c>/</c> 技能、<c>@</c> 成员…）：认出光标处在补什么、给候选。
/// 列表、键盘导航与写回由 <see cref="CommandPaletteViewData"/> 统一做，新增一种补全只加一个实现
/// </summary>
public interface ICompletionSource
{
    /// <summary>
    /// 光标处有没有这种补全要做
    /// </summary>
    /// <param name="text">输入框内容</param>
    /// <param name="caret">光标位置（0..text.Length）</param>
    /// <param name="match">要被替换的范围与查询词</param>
    /// <returns>有为 true</returns>
    bool TryMatch(string text, int caret, out CompletionMatch match);

    /// <summary>按查询词给候选</summary>
    /// <param name="query">查询词</param>
    /// <returns>候选，按展示顺序</returns>
    Task<IReadOnlyList<CompletionCandidate>> GetCandidatesAsync(string query);

    /// <summary>补全收起：丢掉这一次补全期间的缓存</summary>
    void Reset();
}

/// <summary>一次补全要替换的范围 [Start, End) 与查询词</summary>
/// <param name="Start">起点</param>
/// <param name="End">终点（不含）</param>
/// <param name="Query">查询词</param>
public readonly record struct CompletionMatch(int Start, int End, string Query);

/// <summary>补全列表里的一项</summary>
public sealed class CompletionCandidate
{
    /// <summary>列表里显示的名字（带触发符，如 <c>/demo</c>、<c>@初春</c>）</summary>
    public required string Label { get; init; }

    /// <summary>采纳后写进输入框的文字</summary>
    public required string Insertion { get; init; }

    /// <summary>第二行说明</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>名字旁的小标签；没有为 null</summary>
    public string? Tag { get; init; }

    /// <summary>小标签的提示</summary>
    public string? TagTip { get; init; }

    /// <summary>头像；没有为 null</summary>
    public Bitmap? Icon { get; init; }

    /// <summary>有没有小标签</summary>
    public bool HasTag => !string.IsNullOrEmpty(Tag);

    /// <summary>有没有头像</summary>
    public bool HasIcon => Icon != null;

    /// <summary>有没有说明</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}
