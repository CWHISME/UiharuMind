using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Core;

namespace UiharuMind.Services;

// 搜索结果数据模型
public record SearchItem(string Path, string FileName, int LineNumber, string Snippet, bool IsContentSearch);

public class SearchService
{
    public const string HistoryFileName = "search_history.json";

    private SimpleGlobber _globber;
    private SimpleGrepper _grepper;
    private readonly List<string> _searchHistory;

    public SearchService()
    {
        // 历史记录
        _searchHistory = SaveUtility.LoadRootFile<List<string>>(HistoryFileName) ??
                         new List<string>();


        var rootDir = _searchHistory.FirstOrDefault() ?? Environment.CurrentDirectory;
        _globber = new SimpleGlobber(rootDir);
        _grepper = new SimpleGrepper(rootDir);
    }

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

    private static string StripGlobPrefix(string raw)
    {
        if (raw.StartsWith("[FILE] ")) return raw[7..];
        if (raw.StartsWith("[DIR]  ")) return raw[7..];
        return raw;
    }
    
    public async Task<List<SearchItem>> SearchAsync(
        string query,
        bool isContentMode,
        bool isRegex,
        bool caseSensitive,
        CancellationToken ct)
    {
        var results = new List<SearchItem>();

        try
        {
            if (isContentMode)
            {
                // 内容搜索
                var grepResults = await _grepper.SearchAsync(
                    query,
                    isRegex,
                    caseSensitive,
                    contextLines: 2,
                    maxDepth: null,
                    fileGlobs: null,
                    directory: _searchHistory.FirstOrDefault(),
                    ct).ConfigureAwait(false);

                results = grepResults.Select(r => new SearchItem(
                    r.FileName,
                    Path.GetFileName(r.FileName),
                    r.MatchingLines.FirstOrDefault()?.LineNumber ?? 0,
                    r.Snippet,
                    true
                )).ToList();
            }
            else
            {
                var pattern = query.Contains('*') || query.Contains('?') ? query : $"**/*{query}*";
                var globResults = await _globber.SearchAsync(pattern, _searchHistory.FirstOrDefault(), ct: ct).ConfigureAwait(false);

                results = globResults.Select(raw =>
                {
                    var clean = StripGlobPrefix(raw);
                    return new SearchItem(
                        clean,
                        Path.GetFileName(clean),
                        0,
                        raw,
                        false
                    );
                }).ToList();
            }
        }
        catch (OperationCanceledException)
        {
            // 取消搜索是正常行为
        }
        catch (Exception)
        {
            // 简单吞掉异常，避免界面崩溃
        }

        return results;
    }

    private void SaveHistory()
    {
        SaveUtility.SaveRootFile(HistoryFileName, _searchHistory);
    }
}