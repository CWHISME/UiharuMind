namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 模型能选哪些思考档位，以及它有没有对此表过态。
/// </summary>
/// <param name="Levels">档位候选；空表示这个模型没有档位（只有开关，或只认 token 预算）</param>
/// <param name="Declared">模型是否声明过 <c>reasoning_options</c>；
/// false 时 <paramref name="Levels"/> 只是兜底阶梯，不代表模型真的认这些值</param>
public readonly record struct ThinkingProfile(IReadOnlyList<string> Levels, bool Declared);

/// <summary>
/// 思考档位的取值约定。档位不是固定枚举——能接受哪些值由模型自己声明
/// （models.dev 的 <c>reasoning_options</c>，如 <c>low,medium,xhigh</c>），
/// 写死一套 low..max 会让只认三档的模型直接 400、声明了 <c>xhigh</c> 的模型点不到。
///
/// 这里只管两件事：值怎么表示、旧存档怎么读。支持哪些值由
/// <see cref="RemoteOpenAI.ModelCatalog.ModelCapabilityResolver"/> 查表给出。
/// </summary>
public static class ThinkingEfforts
{
    /// <summary>不干预：不发任何思考参数，沿用模型自身行为</summary>
    public const string Auto = "";

    /// <summary>显式关闭思考</summary>
    public const string Off = "none";

    /// <summary>模型没声明可选值时给的兜底阶梯，与改动前的四个档位一致</summary>
    public static readonly IReadOnlyList<string> Fallback = ["low", "medium", "high", "max"];

    /// <summary>
    /// 旧存档存的是 <c>EThinkingMode</c> 的枚举名，读进来要翻成 wire 值。
    /// 其余值按小写原样保留——<c>xhigh</c> 这类服务端自定义值不能被改写。
    /// </summary>
    private static readonly Dictionary<string, string> LegacyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Default"] = Auto,
        ["None"] = Off,
        ["Light"] = "low",
        ["Medium"] = "medium",
        ["High"] = "high",
        ["Max"] = "max",
    };

    /// <summary>
    /// 归一化成 wire 值；空与 <c>Default</c> 都归一成 <see cref="Auto"/>
    /// </summary>
    /// <param name="value">存档或表单里的原始值</param>
    /// <returns>可直接下发的档位值</returns>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Auto;
        string trimmed = value.Trim();
        return LegacyNames.TryGetValue(trimmed, out string? mapped) ? mapped : trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// 解析模型声明的档位表（逗号分隔）。没声明时给兜底阶梯——
    /// 「没声明」不等于「不支持思考」，给空列表会让编辑窗口连档位都不显示。
    /// </summary>
    /// <param name="declared">模型声明的逗号分隔值；空表示未声明</param>
    /// <returns>去重后的档位值列表</returns>
    public static IReadOnlyList<string> ParseDeclared(string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared)) return Fallback;

        List<string> values = [];
        foreach (string part in declared.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string value = Normalize(part);
            if (value.Length > 0 && !values.Contains(value, StringComparer.Ordinal)) values.Add(value);
        }

        return values.Count > 0 ? values : Fallback;
    }

    /// <summary>是否「不干预」</summary>
    /// <param name="value">档位值</param>
    /// <returns>不干预为 true</returns>
    public static bool IsAuto(string? value) => Normalize(value).Length == 0;

    /// <summary>是否「显式关闭」</summary>
    /// <param name="value">档位值</param>
    /// <returns>关闭为 true</returns>
    public static bool IsOff(string? value) => string.Equals(Normalize(value), Off, StringComparison.Ordinal);
}
