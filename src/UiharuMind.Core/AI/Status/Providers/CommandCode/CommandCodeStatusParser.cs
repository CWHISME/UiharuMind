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

    /// <summary>秒级 Unix 时间戳的上限（9999-12-31）；超过它只可能是毫秒</summary>
    private const long MaxUnixSeconds = 253402300799;

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

        return new ProviderStatusSnapshot(CommandCodeStatusSource.ProviderName, meters, balance, [], now,
            ReadAccountName(whoami));
    }

    /// <summary>
    /// 解析 <c>/alpha/usage/summary</c>（2026-10-28 实测）：
    /// <code>
    /// { "totalCount": 43, "totalCost": 0.0315, "successRate": 100,
    ///   "totalCredits": 0.0315, "periodBasis": "billing-period",
    ///   "periodStart": "2026-10-28T10:40:16.000Z", "daily": [] }
    /// </code>
    ///
    /// <b>它没有「本月上限」字段</b>：月度进度的分母只能由「剩余 + 已用」推，
    /// 所以 <c>periodStart</c> 与已用两个读数缺一不可，缺任何一个就整份不用——
    /// 宁可少一行，不可给错的分母。
    /// </summary>
    /// <param name="json">响应体</param>
    /// <returns>统计；读不出必需字段时 null</returns>
    public static ProviderUsageSummary? ParseSummary(string? json)
    {
        if (TryParseNode(json) is not JsonObject summary) return null;

        double? used = ReadNumber(summary, "totalCost", "total_cost", "totalCredits", "total_credits", "cost");
        DateTimeOffset? periodStart = ReadTime(summary, "periodStart", "period_start", "cycleStart", "cycle_start");
        if (used is not >= 0 || periodStart == null) return null;

        string unit = ReadString(summary, "unit", "currency") ?? FallbackUnit;
        int requests = (int)Math.Round(ReadNumber(summary, "totalCount", "total_count", "requestCount") ?? 0);
        double successRate = ReadNumber(summary, "successRate", "success_rate") ?? 0;
        return new ProviderUsageSummary(used.Value, unit, periodStart.Value, requests, successRate);
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

    private static ProviderBalance? ReadBalance(JsonNode credits, JsonNode? whoami, string unit)    {
        // 真实形状把余额拆成三个桶；任何一个存在就按它们求和，没有再退回单字段。
        // 桶本身也一起带上去：界面要能说「本月还剩多少」，合计说不出来
        (string Name, EProviderCreditBucket Kind)[] names =
        [
            ("monthlyCredits", EProviderCreditBucket.Monthly),
            ("purchasedCredits", EProviderCreditBucket.Purchased),
            ("freeCredits", EProviderCreditBucket.Free)
        ];
        List<ProviderCreditBucket> buckets = [];
        foreach ((string name, EProviderCreditBucket kind) in names)
        {
            if (FindNumber(credits, "credits." + name) is { } amount) buckets.Add(new ProviderCreditBucket(kind, amount));
        }

        double? remaining = buckets.Count > 0
            ? buckets.Sum(bucket => bucket.Amount)
            : ReadNumber(credits, "credits.remaining", "remaining", "balance", "credits_remaining",
                "credits_left", "available");

        if (remaining == null && credits is JsonValue value && TryDouble(value, out double flat)) remaining = flat;

        string? planName = ReadPlanName(whoami) ?? ReadPlanNameFromCredits(credits);
        return remaining == null && planName == null
            ? null
            : new ProviderBalance(remaining, unit, planName, buckets.Count > 0 ? buckets : null);
    }

    private static string? ReadPlanName(JsonNode? whoami)    {
        // 实测 whoami 只有 user/org，没有 plan；org 有值时才可能带计划名
        if (whoami is not JsonObject whoamiObject) return null;
        return ReadString(whoamiObject,
            "plan.name", "plan", "plan_name", "planName",
            "subscription.plan", "subscription.planName",
            "org.plan.name", "org.plan", "org.planName");
    }

    private static string? ReadPlanNameFromCredits(JsonNode credits) =>
        ReadString(credits, "plan.name", "plan", "plan_name", "planName");

    /// <summary>
    /// 账户显示名。<b>只取名字，不取邮箱</b>：面板是常驻界面，邮箱截图外传的代价比名字大得多。
    /// 团队账户优先显示组织名（看到的是谁的花费就是团队而不是个人）。
    /// </summary>
    private static string? ReadAccountName(JsonNode? whoami)
    {
        if (whoami is not JsonObject whoamiObject) return null;
        return ReadString(whoamiObject,
            "org.name", "org.displayName",
            "user.name", "user.userName", "user.displayName");
    }

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

    /// <summary>
    /// 读时刻。<b>三个都必须容忍</b>：字符串、秒级数字、<b>毫秒级数字</b>。
    ///
    /// CC 实测给的是毫秒（<c>resetAt: 1793202552382</c>），按秒读会越界抛异常；
    /// 而这个异常以前没有被吞，一抛就把整份快照（含余额与窗口）都废掉——
    /// 一个时间字段读不懂，代价不能是「整块数据都取不到」。
    /// </summary>
    private static DateTimeOffset? ReadTime(JsonNode root, params string[] names)
    {
        JsonNode? node = Select(root, names);
        if (node is not JsonValue value) return null;

        if (value.TryGetValue(out string? text) &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        if (!value.TryGetValue(out long stamp) || stamp <= 0) return null;
        try
        {
            // 按量级判而不是按年份写死：超过秒级上限（9999-12-31）就当毫秒
            return stamp > MaxUnixSeconds
                ? DateTimeOffset.FromUnixTimeMilliseconds(stamp)
                : DateTimeOffset.FromUnixTimeSeconds(stamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
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
