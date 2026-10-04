using System;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Core;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 一个会话的模型用量：占用 / 上限、累计花费、占用进度与模型 tooltip。群成员卡与化身行共用，
/// 外观见 <see cref="SessionUsageBar"/>。数字不自己盯，由持有者在用量上报时调 <see cref="Refresh"/>
/// </summary>
public sealed class SessionUsageStats : ObservableObject
{
    private string? _modelName; //会话钉选的模型；空为跟随全局
    private long _usageTokens; //当前有效占用：最近一次请求输入与固定开销取大
    private long _inputTokens; //会话累计输入
    private long _outputTokens; //会话累计输出

    /// <summary>
    /// 构造（不立即读数，持有者按需 <see cref="Refresh"/>）
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public SessionUsageStats(string sessionId)
    {
        SessionId = sessionId;
        _modelName = SessionManager.Instance.GetMeta(sessionId)?.SessionModelName;
    }

    /// <summary>会话标识</summary>
    public string SessionId { get; }

    /// <summary>固定开销：装配预演得出的每轮最低占用；没预演为 0（只看服务端报的实际占用）</summary>
    public int FixedTokens { get; set; }

    /// <summary>上下文行：占用 / 模型上限（上限未知时只给占用；无占用时为空，由 HasUsage 折叠）</summary>
    public string ContextLine
    {
        get
        {
            if (_usageTokens <= 0) return "";
            int contextLength = ResolveModel()?.ContextLength ?? 0;
            return contextLength > 0
                ? $"{TurnUsageLedger.Format(_usageTokens)} / {TurnUsageLedger.Format(contextLength)}"
                : TurnUsageLedger.Format(_usageTokens);
        }
    }

    /// <summary>累计花费：会话累计输入 + 输出（成本视角，一轮多次工具往返逐次相加）</summary>
    public long SpentTokens => _inputTokens + _outputTokens;

    /// <summary>累计行：与占用同一行靠右；没花过为空，由 HasCost 折叠</summary>
    public string CostLine => HasCost
        ? string.Format(Loc.Text(LangKey.GroupMemberCostFormat), TurnUsageLedger.Format(SpentTokens))
        : "";

    /// <summary>有没有累计可显示（也即调用过模型）</summary>
    public bool HasCost => SpentTokens > 0;

    /// <summary>有没有算出来的占用（占用行与进度条据此显隐）</summary>
    public bool HasUsage => _usageTokens > 0;

    /// <summary>统计行有没有可显示（占用或累计任一段有即显示，容器据此折叠）</summary>
    public bool HasTokenLine => HasUsage || HasCost;

    /// <summary>占用占上限的百分比（进度条；上限未知或没算出来为 0）</summary>
    public double UsagePercent
    {
        get
        {
            int contextLength = ResolveModel()?.ContextLength ?? 0;
            return contextLength > 0 ? Math.Clamp(_usageTokens * 100.0 / contextLength, 0, 100) : 0;
        }
    }

    /// <summary>tooltip：模型名 + 上下文，花过的再补一行输入 / 输出拆分</summary>
    public string Tip
    {
        get
        {
            string tip = string.Format(Loc.Text(LangKey.GroupMemberModelTooltipFormat), EffectiveModelName,
                string.IsNullOrEmpty(ContextLine) ? "—" : ContextLine);
            if (!HasCost) return tip;
            return tip + "\n" + string.Format(Loc.Text(LangKey.GroupMemberCostTooltipFormat),
                TurnUsageLedger.Format(_inputTokens), TurnUsageLedger.Format(_outputTokens));
        }
    }

    /// <summary>实际生效的模型名（钉选 → 全局当前 → 首选，不套「跟随全局」包装）</summary>
    private string EffectiveModelName
    {
        get
        {
            if (!string.IsNullOrEmpty(_modelName)) return _modelName;
            string? name = LlmManager.Instance.CurrentRunningModel?.ModelName
                           ?? LlmManager.Instance.GetPreferredModelName(false);
            return string.IsNullOrEmpty(name) ? "—" : name;
        }
    }

    /// <summary>
    /// 取会话本体的真实占用（<see cref="ChatSession.LastInputTokens"/>，最近一次请求的输入 token），
    /// 与固定开销取大——固定开销是「每轮最低要吃掉多少」，请求输入是服务端报的实际占用，
    /// 两个口径各管一截（见 ADR 0009 的有效占用）。累计花费与模型同一处取
    /// </summary>
    public void Refresh()
    {
        ChatSession? session = SessionManager.Instance.Load(SessionId);
        if (session != null) _modelName = session.SessionModelName;
        _usageTokens = Math.Max(session?.LastInputTokens ?? 0, FixedTokens);
        _inputTokens = session?.TotalInputTokens ?? 0;
        _outputTokens = session?.TotalOutputTokens ?? 0;
        OnPropertyChanged(nameof(SpentTokens));
        OnPropertyChanged(nameof(CostLine));
        OnPropertyChanged(nameof(HasCost));
        OnPropertyChanged(nameof(ContextLine));
        OnPropertyChanged(nameof(UsagePercent));
        OnPropertyChanged(nameof(HasUsage));
        OnPropertyChanged(nameof(HasTokenLine));
        OnPropertyChanged(nameof(Tip));
    }

    /// <summary>会话的模型（钉选解析 → 全局当前）</summary>
    private ModelRunningData? ResolveModel()
    {
        if (!string.IsNullOrEmpty(_modelName)
            && LlmManager.Instance.CacheModelDictionary.TryGetValue(_modelName, out ModelRunningData? pinned))
            return pinned;
        return LlmManager.Instance.CurrentRunningModel;
    }
}
