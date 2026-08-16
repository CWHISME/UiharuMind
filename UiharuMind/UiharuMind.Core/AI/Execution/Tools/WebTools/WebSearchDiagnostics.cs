/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>引擎当前处境</summary>
public enum EWebProviderState
{
    /// <summary>可用</summary>
    Ready,

    /// <summary>缺 API key,兜底链会直接跳过</summary>
    NotConfigured,

    /// <summary>连续失败被熔断,冷却期内不再尝试</summary>
    Cooling
}

/// <summary>
/// 一个搜索引擎的当前状态
/// </summary>
/// <param name="Name">引擎名</param>
/// <param name="Order">在兜底链上的次序,从 1 起</param>
/// <param name="State">当前处境</param>
/// <param name="Cooldown">熔断剩余时长,未熔断为零</param>
/// <param name="LastError">最近一次失败原因</param>
public sealed record WebProviderStatus(
    string Name, int Order, EWebProviderState State, TimeSpan Cooldown, string? LastError);

/// <summary>
/// 一次实测的结果
/// </summary>
/// <param name="Name">引擎名</param>
/// <param name="Ok">是否拿到了结果</param>
/// <param name="ResultCount">结果条数</param>
/// <param name="ElapsedMs">耗时</param>
/// <param name="Detail">不通时的原因</param>
/// <param name="Preview">真抓回来的内容,原样回显给用户看</param>
public sealed record WebProviderProbe(
    string Name, bool Ok, int ResultCount, long ElapsedMs, string? Detail, string? Preview = null);

/// <summary>
/// 搜索链的体检入口,给设置页用。
///
/// 日志能回答"刚才那次搜索走了谁",这里回答"现在这条链上谁还活着"——后者是事前认知,
/// 没有它,引擎悄悄失效(DDG 就是)或正在熔断时,界面上完全看不出来。
/// </summary>
public static class WebSearchDiagnostics
{
    /// <summary>实测用的查询词。刻意用个有把握出结果的普通词,搜不到就说明是引擎的问题</summary>
    private const string ProbeQuery = "wikipedia";

    /// <summary>
    /// 取全链引擎的当前状态,顺序即兜底优先级
    /// </summary>
    /// <returns>各引擎状态</returns>
    public static IReadOnlyList<WebProviderStatus> GetStatuses()
    {
        List<WebProviderStatus> list = [];
        IReadOnlyList<ISearchProvider> providers = FallbackSearchEngine.Shared.Providers;

        for (int i = 0; i < providers.Count; i++)
        {
            ISearchProvider provider = providers[i];
            bool cooling = WebServiceCircuit.IsTripped(provider.Name, out TimeSpan cooldown);
            EWebProviderState state = !provider.IsAvailable ? EWebProviderState.NotConfigured
                : cooling ? EWebProviderState.Cooling
                : EWebProviderState.Ready;

            list.Add(new WebProviderStatus(
                provider.Name, i + 1, state, cooldown, WebServiceCircuit.GetLastError(provider.Name)));
        }

        return list;
    }

    /// <summary>
    /// 挨个实测。各引擎并行跑,互不等待。
    ///
    /// 刻意<b>不</b>把结果计入熔断:手动体检失败就把引擎停掉五分钟,接下来的真实搜索会莫名其妙
    /// 少一环,那是帮倒忙。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>各引擎的实测结果,顺序与 <see cref="GetStatuses"/> 一致</returns>
    public static async Task<IReadOnlyList<WebProviderProbe>> ProbeAllAsync(CancellationToken ct = default)
    {
        IEnumerable<Task<WebProviderProbe>> probes =
            FallbackSearchEngine.Shared.Providers.Select(provider => ProbeOneAsync(provider, ct));
        return await Task.WhenAll(probes).ConfigureAwait(false);
    }

    /// <summary>
    /// 只测一个引擎。调错了名字返回 null,而不是悄悄什么都不做
    /// </summary>
    /// <param name="name">引擎名,取自 <see cref="GetStatuses"/></param>
    /// <param name="ct">取消令牌</param>
    /// <returns>实测结果;链上没有这个名字时为 null</returns>
    public static async Task<WebProviderProbe?> ProbeAsync(string name, CancellationToken ct = default)
    {
        ISearchProvider? provider = FallbackSearchEngine.Shared.Providers
            .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
        return provider == null ? null : await ProbeOneAsync(provider, ct).ConfigureAwait(false);
    }

    private static async Task<WebProviderProbe> ProbeOneAsync(ISearchProvider provider, CancellationToken ct)
    {
        if (!provider.IsAvailable) return new WebProviderProbe(provider.Name, false, 0, 0, "no API key");

        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            IReadOnlyList<SearchResultItem> items =
                await provider.SearchAsync(ProbeQuery, 3, ct).ConfigureAwait(false);
            watch.Stop();
            return new WebProviderProbe(provider.Name, items.Count > 0, items.Count, watch.ElapsedMilliseconds,
                items.Count > 0 ? null : "no results", FormatPreview(items));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            watch.Stop();
            return new WebProviderProbe(provider.Name, false, 0, watch.ElapsedMilliseconds, e.Message);
        }
    }

    /// <summary>
    /// 把抓回来的条目排成能直接看的文本。
    ///
    /// 断言"解析出了 N 条"只能证明代码跑通了,证明不了拿到的是<b>正经东西</b>——
    /// 标题空着、URL 全指向搜索引擎自己、摘要是一句"请开启 JavaScript",条数照样对。
    /// 所以这里把原物摆出来,让人一眼看出真假。
    /// </summary>
    /// <param name="items">抓到的条目</param>
    /// <returns>回显文本;没有条目时为 null</returns>
    private static string? FormatPreview(IReadOnlyList<SearchResultItem> items)
    {
        if (items.Count == 0) return null;

        StringBuilder preview = new();
        for (int i = 0; i < items.Count; i++)
        {
            SearchResultItem item = items[i];
            if (i > 0) preview.AppendLine();
            preview.AppendLine($"{i + 1}. {Clean(item.Title)}");
            preview.AppendLine($"   {Clean(item.Url)}");
            if (!string.IsNullOrWhiteSpace(item.Snippet)) preview.AppendLine($"   {Clean(item.Snippet, 160)}");
        }

        return preview.ToString().TrimEnd();
    }

    /// <summary>压平换行与连续空白并截断,扒页面拿到的摘要经常带一大片缩进</summary>
    private static string Clean(string text, int maxLength = 120)
    {
        string flat = WhitespaceRegex.Replace(text ?? string.Empty, " ").Trim();
        return flat.Length <= maxLength ? flat : $"{flat[..maxLength]}…";
    }

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
}
