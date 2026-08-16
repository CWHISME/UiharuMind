using System;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Memory;

/// <summary>
/// 索引状态的档位。声明顺序即判定优先级：越靠前越先命中。
///
/// 「从未建立」排在「已过期」之前：新建的记忆库两个条件同时成立，
/// 此时说「需要更新」会让人以为已有一份旧索引可用，而实际上一条也检索不到。
/// </summary>
internal enum EMemoryIndexStatus
{
    Updating,
    Error,
    NeverBuilt,
    Dirty,
    Ready
}

/// <summary>判定档位所需的全部状态：运行期的更新中标记 + <see cref="MemoryData"/> 里持久化的索引状态</summary>
internal readonly record struct MemoryIndexState(
    bool IsUpdating,
    string LastIndexError,
    bool IndexDirty,
    DateTime? LastIndexedAt)
{
    /// <summary>
    /// 从记忆数据与更新器状态取快照
    /// </summary>
    /// <param name="memory">记忆库</param>
    /// <param name="isUpdating">该库的索引是否正在更新</param>
    /// <returns>用于判定档位的状态快照</returns>
    public static MemoryIndexState From(MemoryData memory, bool isUpdating) =>
        new(isUpdating, memory.LastIndexError, memory.IndexDirty, memory.LastIndexedAt);
}

/// <summary>一次判定的结果：档位 + 各处要显示的文案，两个记忆窗口直接取用，不再各算一遍</summary>
internal sealed record MemoryIndexStatusView(
    EMemoryIndexStatus Status,
    string StatusKey,
    string StatusText,
    string DetailText,
    string LastIndexedText);

/// <summary>
/// 记忆索引在界面上的全部文案与状态判定。
///
/// 阶梯（更新中 → 出错 → 从未建立 → 已过期 → 就绪）只有这一份：
/// 原先编辑窗连写三遍、选择窗又写一遍且顺序不同，同一个库在两个窗口里显示不一样。
/// </summary>
internal static class MemoryIndexUiText
{
    /// <summary>
    /// 判定当前档位
    /// </summary>
    /// <param name="state">索引状态快照</param>
    /// <returns>命中的档位</returns>
    public static EMemoryIndexStatus ResolveStatus(MemoryIndexState state)
    {
        if (state.IsUpdating) return EMemoryIndexStatus.Updating;
        if (!string.IsNullOrWhiteSpace(state.LastIndexError)) return EMemoryIndexStatus.Error;
        if (state.LastIndexedAt == null) return EMemoryIndexStatus.NeverBuilt;
        return state.IndexDirty ? EMemoryIndexStatus.Dirty : EMemoryIndexStatus.Ready;
    }

    /// <summary>
    /// 判定档位并一次算好各处文案
    /// </summary>
    /// <param name="state">索引状态快照</param>
    /// <returns>档位与对应文案</returns>
    public static MemoryIndexStatusView ResolveStatusView(MemoryIndexState state)
    {
        EMemoryIndexStatus status = ResolveStatus(state);
        return new MemoryIndexStatusView(
            status,
            GetStatusKey(status),
            Loc.Text(GetStatusTextKey(status)),
            status == EMemoryIndexStatus.Error
                ? GetIndexErrorText(state.LastIndexError)
                : Loc.Text(GetStatusDetailTextKey(status)),
            GetLastIndexedText(state.LastIndexedAt));
    }

    /// <summary>
    /// 状态圆点与胶囊的样式 Tag（见 Assets/Themes/CustomStatusStyle.axaml）
    /// </summary>
    /// <param name="status">档位</param>
    /// <returns>样式 Tag；「从未建立」与「已过期」共用同一种待处理配色</returns>
    public static string GetStatusKey(EMemoryIndexStatus status) => status switch
    {
        EMemoryIndexStatus.Updating => "Progress",
        EMemoryIndexStatus.Error => "Error",
        EMemoryIndexStatus.Ready => "Ready",
        _ => "Dirty"
    };

    /// <summary>
    /// 一行短状态的文案键（胶囊、列表项提示）
    /// </summary>
    /// <param name="status">档位</param>
    /// <returns>资源键</returns>
    public static string GetStatusTextKey(EMemoryIndexStatus status) => status switch
    {
        EMemoryIndexStatus.Updating => "MemoryIndexUpdating",
        EMemoryIndexStatus.Error => "MemoryIndexHasError",
        EMemoryIndexStatus.NeverBuilt => "MemoryIndexNotBuiltShort",
        EMemoryIndexStatus.Dirty => "MemoryIndexPendingShort",
        _ => "MemoryIndexReady"
    };

    /// <summary>
    /// 状态详述的文案键。出错档没有固定键——文案取自后端错误，见 <see cref="GetIndexErrorText"/>
    /// </summary>
    /// <param name="status">档位</param>
    /// <returns>资源键</returns>
    public static string GetStatusDetailTextKey(EMemoryIndexStatus status) => status switch
    {
        EMemoryIndexStatus.Updating => "MemoryIndexUpdatingDetail",
        EMemoryIndexStatus.NeverBuilt => "MemoryIndexNotBuilt",
        EMemoryIndexStatus.Dirty => "MemoryIndexNeedUpdate",
        _ => "MemoryIndexReadyDetail"
    };

    /// <summary>
    /// 上次索引时间。两窗统一为纯值，标签前缀由各自版面自己加
    /// </summary>
    /// <param name="lastIndexedAt">上次索引完成时间（UTC）</param>
    /// <returns>本地时间文本；从未索引时返回对应文案</returns>
    public static string GetLastIndexedText(DateTime? lastIndexedAt)
    {
        return lastIndexedAt == null
            ? Loc.Text("MemoryIndexNeverUpdated")
            : lastIndexedAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
    }

    /// <summary>
    /// 来源读取失败的文案
    /// </summary>
    /// <param name="errorCode">来源错误码（就是资源键）</param>
    /// <param name="detail">附加细节，可为空</param>
    /// <returns>可展示的文案</returns>
    public static string GetSourceErrorText(string errorCode, string detail)
    {
        string text = Loc.Text(errorCode);
        return string.IsNullOrWhiteSpace(detail) ? text : $"{text} ({detail})";
    }

    /// <summary>
    /// 索引失败的文案
    /// </summary>
    /// <param name="error">后端给出的英文错误</param>
    /// <returns>认得出来时给译文，认不出来时原样返回后端英文</returns>
    public static string GetIndexErrorText(string error)
    {
        string? key = GetIndexErrorTextKey(error);
        return key == null ? error : Loc.Text(key);
    }

    /// <summary>
    /// 后端错误 → 文案键。按英文前缀匹配，上游改措辞这里就会静默失配，
    /// 因此每条前缀都有测试钉住（见 App.Tests 的 MemoryIndexStatusTests）
    /// </summary>
    /// <param name="error">后端给出的英文错误</param>
    /// <returns>资源键；认不出来时返回 null</returns>
    public static string? GetIndexErrorTextKey(string error)
    {
        if (error.StartsWith("Embedding request failed", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("LLamaSharp embedding request failed", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("Response status code does not indicate success",
                StringComparison.OrdinalIgnoreCase))
        {
            return "MemoryIndexEmbeddingRequestFailed";
        }

        if (error.StartsWith("Embedding model startup failed", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("Failed to load LLamaSharp embedding model", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("Remote embedding backend is not implemented", StringComparison.OrdinalIgnoreCase))
        {
            return "MemoryIndexEmbeddingServerUnavailable";
        }

        if (error.Contains("vector store failed", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("readonly database", StringComparison.OrdinalIgnoreCase))
        {
            return "MemoryIndexStorageFailed";
        }

        return error switch
        {
            "Embedding server is unavailable." => "MemoryIndexEmbeddingServerUnavailable",
            "Embedding model is unavailable." => "MemoryIndexEmbeddingServerUnavailable",
            "Embedding server startup timed out." => "MemoryIndexEmbeddingServerTimeout",
            "Memory name not set" => "MemoryIndexMemoryNameMissing",
            "Memory source validation failed" => "MemorySourceValidationFailed",
            "Memory vector dimension mismatch" => "MemoryIndexDimensionMismatch",
            "Embedding input is too large" => "MemoryIndexEmbeddingInputTooLarge",
            _ => null
        };
    }
}
