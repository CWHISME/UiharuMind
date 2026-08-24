using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Features.QuickTools;

// 搜索结果数据模型
public record SearchItem(string Path, string FileName, int LineNumber, string Snippet, bool IsContentSearch);

/// <summary>
/// 一次界面搜索的结果：命中与失败原因分开。
///
/// 从前失败是混在命中里的字符串（<c>"[Error] 目录不存在"</c> 会被当成一个文件名显示成一条结果），
/// 而真异常被 <c>catch (Exception) { }</c> 静默吞掉——用户看到的是「搜了半天 0 结果」，
/// 与确实无命中完全无法区分。现在两者分开，由界面层各自渲染。
/// </summary>
/// <param name="Items">命中条目</param>
/// <param name="Failure">搜索器给出的失败原因；成功则为 null</param>
/// <param name="ErrorDetail">意外异常的说明；没有则为 null</param>
public record SearchOutcome(List<SearchItem> Items, SearchFailure? Failure, string? ErrorDetail);

public class SearchService
{
    private readonly List<string> _searchHistory;

    public SearchService()
    {
        // 历史记录
        _searchHistory = SaveUtility.Load<List<string>>(AppPaths.Data.QuickSearchHistory) ??
                         new List<string>();


    }

    /// <summary>
    /// 当前搜索根。<b>每次搜索现取</b>：两个搜索器从前是构造时建好并长期持有的，
    /// 而它们的根取自当时的历史首项，用户切了目录之后就成了陈旧值——
    /// 现在搜索结果的路径是相对搜索器的根算的，根一旦陈旧，界面拼出来的完整路径就是错的。
    /// </summary>
    private string CurrentRoot => _searchHistory.FirstOrDefault() ?? Environment.CurrentDirectory;

    public IReadOnlyList<string> GetHistory() => _searchHistory.AsReadOnly();

    public void AddHistory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        _searchHistory.Remove(path);
        _searchHistory.Insert(0, path);
        if (_searchHistory.Count > 10) _searchHistory.RemoveAt(_searchHistory.Count - 1);
        SaveHistory();
    }

    public void RemoveHistory(string path)
    {
        if (_searchHistory.Remove(path)) SaveHistory();
    }

    /// <summary>
    /// 执行一次搜索
    /// </summary>
    /// <param name="query">查询内容</param>
    /// <param name="isContentMode">true 搜内容，false 搜文件名</param>
    /// <param name="isRegex">内容搜索时是否按正则</param>
    /// <param name="caseSensitive">是否区分大小写</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>命中与失败原因</returns>
    public async Task<SearchOutcome> SearchAsync(
        string query,
        bool isContentMode,
        bool isRegex,
        bool caseSensitive,
        CancellationToken ct)
    {
        try
        {
            if (isContentMode)
            {
                // 搜索根即搜索器的根,故不再另传 directory:两者一致才能保证
                // 回来的相对路径与界面拼接用的 CurrentDirectory 同一个基准
                GrepOutcome grep = await new SimpleGrepper(CurrentRoot).SearchAsync(
                    query,
                    isRegex,
                    caseSensitive,
                    contextLines: 2,
                    maxDepth: null,
                    fileGlobs: null,
                    directory: null,
                    ct).ConfigureAwait(false);

                if (grep.Failure != null) return new SearchOutcome([], grep.Failure, null);

                return new SearchOutcome(grep.Matches.Select(r => new SearchItem(
                    r.FileName,
                    Path.GetFileName(r.FileName),
                    r.MatchingLines.FirstOrDefault()?.LineNumber ?? 0,
                    r.Snippet,
                    true
                )).ToList(), null, null);
            }

            var pattern = query.Contains('*') || query.Contains('?') ? query : $"**/*{query}*";
            GlobOutcome glob = await new SimpleGlobber(CurrentRoot)
                .SearchAsync(pattern, directory: null, ct: ct).ConfigureAwait(false);

            if (glob.Failure != null) return new SearchOutcome([], glob.Failure, null);

            // 直接用结构化字段。从前这里靠剥 "[FILE] " 前缀取路径,
            // 于是模型那侧一改渲染格式(比如追加文件大小)界面就静默坏掉
            return new SearchOutcome(glob.Entries.Select(entry => new SearchItem(
                entry.Path,
                Path.GetFileName(entry.Path),
                0,
                entry.IsDirectory ? entry.Path : $"{entry.Path}  ({GameUtils.FormatBytes(entry.SizeBytes)})",
                false
            )).ToList(), null, null);
        }
        catch (OperationCanceledException)
        {
            throw; //取消由界面层当正常流程处理,不是一次失败
        }
        catch (Exception e)
        {
            // 不再静默吞掉:静默的后果是用户看到"0 结果",与确实无命中分不开
            return new SearchOutcome([], null, e.Message);
        }
    }

    private void SaveHistory()
    {
        SaveUtility.Save(AppPaths.Data.QuickSearchHistory, _searchHistory);
    }
}