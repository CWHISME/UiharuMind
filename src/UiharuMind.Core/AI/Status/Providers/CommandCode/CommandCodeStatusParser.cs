using System.Globalization;
using System.Text.Json.Nodes;

namespace UiharuMind.Core.AI.Status.Providers.CommandCode;

/// <summary>
/// CC 的 <c>/alpha/billing/credits</c> 与 <c>/alpha/whoami</c> 响应解析。
///
/// 真实形状（2026-10-27 实测，见 <c>Core.Tests/AI/Status/fixtures/</c>）：
/// <code>
/// { "credits": { "belowThreshold": false, "creditThreshold": 0,
///                "monthlyCredits": 0, "purchasedCredits": 0, "freeCredits": 0 },
///   "windowLimits": { "limited": false, "exceeded": null, "fiveHour": null, "weekly": null },
///   "sandboxAccess": false, "sandboxMinutes": null }
/// </code>
/// 两个要点：<b>窗口在 <c>windowLimits</c> 下、可以整个是 null</b>（免费账户没有滚动窗口），
/// <b>余额被拆成三个桶</b>（月度 / 购买 / 赠送），没有单一的 remaining 字段。
///
/// 窗口对象非 null 时内部字段名仍未实测（fixtures 里 <c>-pro</c> 那份是假拟的），
/// 所以字段按候选路径逐个试、数字兼容字符串、缺字段就少一行、整份读不出结构就返回 null。
/// 端点与字段都可能变，真响应变了只收紧这里，不碰上层。
///
/// 单独成类不只是为了整洁：容错逻辑是这套方案里最容易写错的地方，抽出来才测得到。
/// </summary>
internal static class CommandCodeStatusParser
{
    private const string FallbackUnit = "$";

    /// <summary>
    /// 解析两份响应
    /// </summary>
    /// <param name="creditsJson">/alpha/billing/credits 的响应体</param>
    /// <param name="whoamiJson">/alpha/whoami 的响应体（可空，失败不影响主数据）</param>
    /// <param name="now">取数时刻（写进快照，界面按它算数据年龄）</param>
    /// <returns>快照；一个可用数字都没有时返回 null</returns>
    public static ProviderStatusSnapshot? Parse(string? creditsJson, string? whoamiJson, DateTimeOffset now)
    {
        JsonNode? credits = TryParseNode(creditsJson);
        JsonNode? whoami = TryParseNode(whoamiJson);
        if (credits == null) return null;

        string unit = ReadString(credits, "unit", "currency") ?? FallbackUnit;
        // 实测窗口在 windowLimits 下；接口要是哪天把窗口摊到顶层，也照样认（credits 自己当兜底那一层）
        JsonNode? windows = Select(credits, "windowLimits", "window_limits", "limits", "windows", "credits")
                          ?? credits;

        List<ProviderUsageMeter> meters = [];
        AddMeter(meters, EProviderMeterSlot.FiveHour, windows, unit, "fiveHour", "five_hour", "fiveHours");
        AddMeter(meters, EProviderMeterSlot.Weekly, windows, unit, "weekly", "week", "sevenDay", "seven_day");
        ApplyExceededFlag(meters, windows);

        ProviderBalance? balance = ReadBalance(credits, whoami, unit);
        if (meters.Count == 0 && balance == null) return null;

        return new ProviderStatusSnapshot(CommandCodeStatusSource.ProviderName, meters, balance, [], now);
    }

    private static void AddMeter(List<ProviderUsageMeter> meters, EProviderMeterSlot slot, JsonNode? windows,
        string unit, params string[] names)
    {
        if (windows is not JsonObject obj) return;

        JsonNode? node = Select(obj, names);
        if (node is not JsonObject window) return;

        // 上限是必需的：没有上限的窗口画不出「还剩多少」，也就没有百分比可显示
        double? cap = ReadNumber(window, "limit", "cap", "max", "quota", "total", "allowance",
            "creditsLimit", "limitCredits");
        if (cap is not > 0) return;

        double used = ReadNumber(window, "used", "consumed", "spent", "usage", "count",
            "creditsUsed", "usedCredits") ?? 0;
        DateTimeOffset? resetAt = ReadTime(window, "resetsAt", "resetAt", "reset_at", "resets_at",
            "window_resets_at", "reset");
        bool exceeded = ReadBool(window, "exceeded", "exhausted", "blocked") ?? used >= cap.Value;

        meters.Add(new ProviderUsageMeter(slot, used, cap.Value, unit, resetAt, exceeded));
    }

    /// <summary>
    /// <c>windowLimits.exceeded</c>：实测为空；给了就认——布尔值表示全超，
    /// 字符串 / 数组 / 对象表示点名了哪几个窗口
    /// </summary>
    private static void ApplyExceededFlag(List<ProviderUsageMeter> meters, JsonNode? windows)
    {
        if (windows is not JsonObject obj || meters.Count == 0) return;
        JsonNode? node = Select(obj, "exceeded", "blocked");
        if (node == null) return;

        bool markAll = false;
        List<string> named = [];
        switch (node)
        {
            case JsonValue value when value.TryGetValue(out bool flag):
                markAll = flag;
                break;
            case JsonArray array:
                named.AddRange(array.Select(item => Normalize(item?.ToString() ?? "")).Where(name => name.Length > 0));
                break;
            case JsonObject exceededObject:
                // {"fiveHour": true, "weekly": false} 或 {"fiveHour": {...}}
                named.AddRange(exceededObject
                    .Where(pair => !IsFalseValue(pair.Value))
                    .Select(pair => Normalize(pair.Key)));
                break;
            case JsonValue value when value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text):
                named.Add(Normalize(text));
                break;
        }

        for (int i = 0; i < meters.Count; i++)
        {
            if (markAll || named.Contains(Normalize(meters[i].Slot.ToString())))
            {
                meters[i] = meters[i] with { Exceeded = true };
            }
        }
    }

    private static bool IsFalseValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out bool flag) && !flag;

    private static ProviderBalance? ReadBalance(JsonNode credits, JsonNode? whoami, string unit)
    {
        // 真实形状把余额拆成三个桶；任何一个存在就按它们求和，没有再退回单字段
        string[] buckets = ["monthlyCredits", "purchasedCredits", "freeCredits"];
        bool hasBucket = buckets.Any(bucket => FindNumber(credits, "credits." + bucket) != null);
        double? remaining = hasBucket
            ? buckets.Sum(bucket => FindNumber(credits, "credits." + bucket) ?? 0)
            : ReadNumber(credits, "credits.remaining", "remaining", "balance", "credits_remaining",
                "credits_left", "available");

        if (remaining == null && credits is JsonValue value && TryDouble(value, out double flat)) remaining = flat;

        string? planName = ReadPlanName(whoami) ?? ReadPlanNameFromCredits(credits);
        return remaining == null && planName == null ? null : new ProviderBalance(remaining, unit, planName);
    }

    private static string? ReadPlanName(JsonNode? whoami)
    {
        // 实测 whoami 只有 user/org，没有 plan；org 有值时才可能带计划名
        if (whoami is not JsonObject whoamiObject) return null;
        return ReadString(whoamiObject,
            "plan.name", "plan", "plan_name", "planName",
            "subscription.plan", "subscription.planName",
            "org.plan.name", "org.plan", "org.planName");
    }

    private static string? ReadPlanNameFromCredits(JsonNode credits) =>
        ReadString(credits, "plan.name", "plan", "plan_name", "planName");

    //================= 容错读取 =================

    private static JsonNode? TryParseNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>按候选路径取第一个存在的节点</summary>
    private static JsonNode? Select(JsonNode root, params string[] paths)
    {
        foreach (string path in paths)
        {
            JsonNode? node = SelectPath(root, path);
            if (node != null) return node;
        }

        return null;
    }

    private static JsonNode? SelectPath(JsonNode root, string path)
    {
        JsonNode? node = root;
        foreach (string part in path.Split('.'))
        {
            if (node is not JsonObject obj) return null;
            node = FindProperty(obj, part);
            if (node == null) return null;
        }

        return node;
    }

    private static JsonNode? FindProperty(JsonObject obj, string name)
    {
        if (obj.TryGetPropertyValue(name, out JsonNode? exact)) return exact;

        // snake_case / camelCase 两种写法都可能出现，键名一律忽略大小写与下划线
        string wanted = Normalize(name);
        foreach (KeyValuePair<string, JsonNode?> pair in obj)
        {
            if (Normalize(pair.Key) == wanted) return pair.Value;
        }

        return null;
    }

    private static string Normalize(string key) => key.Replace("_", string.Empty).ToLowerInvariant();

    private static double? FindNumber(JsonNode root, string path)
    {
        JsonNode? node = SelectPath(root, path);
        return node is JsonValue value && TryDouble(value, out double number) ? number : null;
    }

    private static double? ReadNumber(JsonNode root, params string[] names)
    {
        JsonNode? node = Select(root, names);
        return node is JsonValue value && TryDouble(value, out double number) ? number : null;
    }

    private static string? ReadString(JsonNode root, params string[] names)
    {
        JsonNode? node = Select(root, names);
        return node is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
    }

    private static bool? ReadBool(JsonNode root, params string[] names)
    {
        JsonNode? node = Select(root, names);
        return node is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;
    }

    private static DateTimeOffset? ReadTime(JsonNode root, params string[] names)
    {
        JsonNode? node = Select(root, names);
        if (node is not JsonValue value) return null;

        if (value.TryGetValue(out string? text) &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        if (value.TryGetValue(out long epoch) && epoch > 0) return DateTimeOffset.FromUnixTimeSeconds(epoch);
        return null;
    }

    private static bool TryDouble(JsonValue value, out double number)
    {
        if (value.TryGetValue(out number)) return true;
        // 数字兼容字符串形式（"12.4" / "$12.40"）
        if (!value.TryGetValue(out string? text) || string.IsNullOrWhiteSpace(text))
        {
            number = 0;
            return false;
        }

        text = text.Trim().TrimStart('$').Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }
}
