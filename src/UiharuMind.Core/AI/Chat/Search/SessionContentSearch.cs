/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat.Search;

/// <summary>
/// 跨会话搜索的一条命中。只留列表要显示的那点东西，不挂消息本体——
/// 常见词能在几百个会话里各中几十条，挂着消息等于扫一遍就把这些历史全解析进内存
/// </summary>
/// <param name="MessageIndex">
/// 历史文件里非空行的行序。与装载后的历史下标大体一致，但不保证（损坏行装载时跳过、这里照数；
/// 装载还会收掉重复与悬空的工具结果），所以打开会话后按关键词在会话内再搜一次、取下标最近的那条
/// </param>
/// <param name="Kind">落在哪一部分</param>
/// <param name="IsUser">是不是用户说的</param>
/// <param name="Snippet">命中前后的一小段</param>
public readonly record struct SessionContentHit(int MessageIndex, ESearchHitKind Kind, bool IsUser, string Snippet);

/// <summary>一个会话里的命中</summary>
/// <param name="SessionId">会话标识</param>
/// <param name="HitCount">命中总数</param>
/// <param name="Latest">最近的几条，新到旧</param>
public sealed record SessionContentMatch(string SessionId, int HitCount, IReadOnlyList<SessionContentHit> Latest);

/// <summary>
/// 跨会话全文搜索：逐个会话读历史文件、按会话内搜索同一口径匹配（见 ADR 0057）。
///
/// 不建索引。实测 513 份历史 206MB，按文件并行、先按行预筛再解析，一遍 80～390ms；
/// 读文件本身就占约 210ms 的单线程下限，索引省下的只有解析那一截，换来的是写入时多一份要保持一致的数据
/// </summary>
public static class SessionContentSearch
{
    // 历史文件与 HistoryJsonl 同一个编码器:关键词按它编码后才能直接在原始行上找
    private static readonly JavaScriptEncoder LineEncoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>
    /// 扫一批会话。并行读，结果按传入顺序排（调用方给的就是列表顺序）。
    /// 单个会话读不了（被删、被占、权限）就当没搜到，不拖垮整次搜索
    /// </summary>
    /// <param name="sessionIds">要扫的会话，按显示顺序</param>
    /// <param name="readLines">读一个会话的历史行（<see cref="SessionManager.ReadHistoryLines"/>；测试喂内存里的行）</param>
    /// <param name="query">关键词；空白返回空</param>
    /// <param name="latestPerSession">每个会话留最近几条</param>
    /// <param name="options">搜索范围</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>有命中的会话</returns>
    public static List<SessionContentMatch> Scan(IReadOnlyList<string> sessionIds,
        Func<string, IEnumerable<string>> readLines, string? query, int latestPerSession,
        SessionSearchOptions options = default, CancellationToken cancellationToken = default)
    {
        if (SessionSearch.KeywordOf(query) is not { } keyword) return [];

        string? prefilter = PrefilterOf(keyword);
        SessionContentMatch?[] matches = new SessionContentMatch?[sessionIds.Count];
        // 读文件占大头,开一半核就够把它压下去,剩下的留给界面线程与正在跑的轮
        ParallelOptions parallel = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
            CancellationToken = cancellationToken,
        };
        Parallel.For(0, sessionIds.Count, parallel, i =>
        {
            string sessionId = sessionIds[i];
            try
            {
                matches[i] = ScanSession(sessionId, readLines(sessionId), keyword, prefilter, latestPerSession,
                    options, cancellationToken);
            }
            catch (Exception e)
            {
                // 读到一半被删、被整份重写、没权限,解析时撞上意外的内容:这一个算没搜到
                Log.Debug($"Content search skipped session '{sessionId}': {e.GetType().Name}: {e.Message}");
            }
        });

        cancellationToken.ThrowIfCancellationRequested();
        return matches.OfType<SessionContentMatch>().ToList();
    }

    /// <summary>
    /// 原始行上的预筛词。关键词里有编码器会转义的字符（引号、反斜杠、控制符、emoji 这类代理对）时返回 null，
    /// 整行解析：工具结果里嵌着的 JSON 会再转义一层，按编码后的样子也未必找得到，宁可慢也别漏
    /// </summary>
    internal static string? PrefilterOf(string keyword)
    {
        string encoded = JsonEncodedText.Encode(keyword, LineEncoder).Value;
        return encoded == keyword ? keyword : null;
    }

    // 枚举在这里才打开文件(ReadHistoryLines 是迭代器),打开失败也落在调用方的 try 里
    private static SessionContentMatch? ScanSession(string sessionId, IEnumerable<string> lines, string keyword,
        string? prefilter, int latestPerSession, SessionSearchOptions options, CancellationToken cancellationToken)
    {
        Queue<SessionContentHit> latest = new(latestPerSession + 1); //文件从旧到新,只留最后几条
        int count = 0;
        int index = -1;
        foreach (string line in lines)
        {
            // 不在循环体里抛:并行体抛出的取消会被包成 AggregateException,统一在汇总处抛
            if (cancellationToken.IsCancellationRequested) return null;
            if (string.IsNullOrWhiteSpace(line)) continue; //与 HistoryJsonl.Parse 同样跳空行,行序才对得上下标
            index++;
            if (prefilter != null && !line.Contains(prefilter, StringComparison.OrdinalIgnoreCase)) continue;
            if (HistoryJsonl.TryParseLine(line) is not { } message) continue;
            if (SessionSearch.FindIn(message, index, keyword, options) is not { } hit) continue;

            count++;
            latest.Enqueue(new SessionContentHit(index, hit.Kind, message.Role == ChatRole.User, hit.Snippet));
            if (latest.Count > latestPerSession) latest.Dequeue();
        }

        return count == 0 ? null : new SessionContentMatch(sessionId, count, latest.Reverse().ToList());
    }
}
