using System.Linq;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 子会话标识的「模型可见短号 ↔ 真实 ID」别名层。
///
/// 存储、存档文件名、内存集合一律沿用真实 ID（<c>Guid.ToString("N")</c>，32 位十六进制），
/// 一行不动；只有出现在模型面前的那几条缝——回执、报告信头、交接文档清单——换成前 8 位短号，
/// 让它少抄一点、少抄错一点。
///
/// 短号<b>不是新 ID</b>：由真实 ID 派生，重启天然稳定，无需持久化、不碰序列化兼容。
/// 反查按前缀匹配（不分大小写，模型会把短号重打成大写）。撞号概率：同一父会话下约百个子会话时
/// 约百万分之一；不限父会话的全局反查候选是历史累计的全部子会话，概率随之上升。
/// 撞上时由调用方把各候选的完整 ID 交给模型挑——它手里只有 8 位，「给更多位」它给不出来。
/// </summary>
public static class SubSessionIdAlias
{
    /// <summary>模型可见的短号长度（真实 ID 前 8 位）</summary>
    private const int ShortLength = 8;

    /// <summary>
    /// 模型可见的短号：真实 ID 前 8 位；本来就短的标识原样返回。
    /// </summary>
    /// <param name="fullId">真实会话标识</param>
    /// <returns>短号</returns>
    public static string Short(string fullId) =>
        fullId.Length <= ShortLength ? fullId : fullId[..ShortLength];

    /// <summary>
    /// 短号（或完整 ID）反查子会话，返回全部命中：一个即唯一命中，零个即查无此人，多个即撞号。
    /// </summary>
    /// <param name="parentSessionId">限定在哪个父会话的子会话里查；为空则全局按子会话查</param>
    /// <param name="shortOrFullId">模型粘回来的短号或完整 ID</param>
    /// <returns>命中的子会话元数据</returns>
    public static IReadOnlyList<ChatSessionMeta> Match(string? parentSessionId, string shortOrFullId)
    {
        if (string.IsNullOrWhiteSpace(shortOrFullId)) return [];
        bool scoped = !string.IsNullOrEmpty(parentSessionId);

        // 完整 ID 精确命中直接返回：老历史里存的就是完整 ID，也省一次全量扫描
        if (SessionManager.Instance.GetMeta(shortOrFullId) is { IsSubSession: true } exact)
        {
            return !scoped || exact.ParentSessionId == parentSessionId ? [exact] : [];
        }

        List<ChatSessionMeta> hits = FindByPrefix(parentSessionId, shortOrFullId);
        // 模型见了 8 位十六进制会当成截断的 GUID 自己补全(实测 76ebc8ee → 76ebc8ee-8ee4-4a9b-…),
        // 补出来的后半截对不上任何会话。退回前 8 位再认一次,别让它因此另起新人
        if (hits.Count == 0 && shortOrFullId.Length > ShortLength)
        {
            hits = FindByPrefix(parentSessionId, Short(shortOrFullId));
        }

        return hits;
    }

    private static List<ChatSessionMeta> FindByPrefix(string? parentSessionId, string prefix)
    {
        bool scoped = !string.IsNullOrEmpty(parentSessionId);
        return SessionManager.Instance.FindSessions(x =>
            x.IsSubSession
            && (!scoped || x.ParentSessionId == parentSessionId)
            && x.SessionId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
