#!/usr/bin/env python3
"""Generate ModelCapabilityTable.g.cs from models.dev/api.json.

models.dev is a build-time capability DB (MIT). We keep two things:

1. Entries keyed by ``provider/modelId`` — what the model editor prefills
   (vision, max output, context, fixed temperature) plus what the list shows
   (tool calling, thinking, price).
2. Canonical entries keyed by ``canonical_model_id`` — the cross-provider
   fallback. Resellers ship stale snapshots: a gateway may list a model that
   models.dev has never seen under that provider, and without the fallback
   every capability degrades to "false". Vision is the field that actually
   breaks things, so the index carries it plus the two window sizes.

Prices are USD per million tokens. ``tiers`` / ``context_over_200k`` are the
"long context costs more" step and are kept alongside the base price.

Run this when adding a provider or refreshing the table; commit the generated
.g.cs together with the script.

Usage:
  python generate_modelsdev_capabilities.py
  python generate_modelsdev_capabilities.py --input /path/to/api.json
"""

import argparse
import json
import os
import sys
import urllib.request

PROVIDERS = ["agnes", "deepseek", "sensenova", "zhipuai", "commandcode", "opencode", "opencode-go"]
URL = "https://models.dev/api.json"

# 配置类型 → models.dev provider 键。新增供应商时同步这里。
PROVIDER_TYPES = [
    ("RemoteAgnesModelConfig", "agnes"),
    ("RemoteDeepSeekModelConfig", "deepseek"),
    ("RemoteSensenovaModelConfig", "sensenova"),
    ("RemoteZhipuModelConfig", "zhipuai"),
    ("RemoteCommandCodeModelConfig", "commandcode"),
    ("RemoteOpenCodeModelConfig", "opencode"),
]


def load(input_path):
    if input_path:
        with open(input_path, encoding="utf-8") as f:
            return json.load(f)
    with urllib.request.urlopen(URL, timeout=60) as r:
        return json.load(r)


def escape(value):
    return value.replace("\\", "\\\\").replace('"', '\\"')


def money(value):
    """C# decimal 字面量：保留到 6 位，去掉尾零。"""
    if not value:
        return "0m"
    text = f"{float(value):.6f}".rstrip("0").rstrip(".")
    return f"{text}m"


def efforts_of(model):
    """思考档位：reasoning_options 里 effort 型声明的取值表，按声明顺序去重。"""
    seen = []
    for option in model.get("reasoning_options") or []:
        for value in option.get("values") or []:
            if value not in seen:
                seen.append(value)
    return ",".join(seen)


def thinking_control(model):
    """思考控制形态：有取值表按档位选（effort）、只有开关（toggle）、只给 token 预算（budget）。

    混合声明时按「有没有档位可选」定：effort+toggle 算 effort，toggle+budget 算 toggle。
    没声明就是 Unknown——不等于不支持思考，消费侧按兜底阶梯处理。
    """
    types = {o.get("type") for o in model.get("reasoning_options") or []}
    if not types:
        return "Unknown"
    if "effort" in types:
        return "Effort"
    if "toggle" in types:
        return "Toggle"
    return "Budget"


def price_of(model):
    """(base 四价, 阶梯起点, 阶梯四价)。没有公布就是全零，IsKnown 为 false。"""
    cost = model.get("cost") or {}
    base = (
        cost.get("input") or 0,
        cost.get("output") or 0,
        cost.get("cache_read") or 0,
        cost.get("cache_write") or 0,
    )

    tiers = cost.get("tiers") or []
    if tiers:
        tier = tiers[0]
        start = int((tier.get("tier") or {}).get("size") or 0)
        step = (
            tier.get("input") or 0,
            tier.get("output") or 0,
            tier.get("cache_read") or 0,
            tier.get("cache_write") or 0,
        )
        return base, start, step

    # 老字段：只有"超过 20 万"这一档，没有显式起点
    over = cost.get("context_over_200k")
    if over:
        step = (
            over.get("input") or 0,
            over.get("output") or 0,
            over.get("cache_read") or 0,
            over.get("cache_write") or 0,
        )
        return base, 200000, step

    return base, 0, (0, 0, 0, 0)


def is_vision(model):
    modalities = model.get("modalities") or {}
    return "image" in (modalities.get("input") or [])


def windows(model):
    limit = model.get("limit") or {}
    return int(limit.get("context") or 0), int(limit.get("output") or 0)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", help="本地 api.json；缺省从 models.dev 下载")
    parser.add_argument(
        "--output",
        default=os.path.join(
            os.path.dirname(__file__), "..", "..", "UiharuMind.Core", "RemoteOpenAI", "ModelCatalog",
            "ModelCapabilityTable.g.cs",
        ),
    )
    args = parser.parse_args()

    data = load(args.input)

    rows = []
    for provider in PROVIDERS:
        p = data.get(provider)
        if not p:
            print(f"skip missing provider: {provider}", file=sys.stderr)
            continue
        for model_id, m in (p.get("models") or {}).items():
            ctx, max_out = windows(m)
            base, tier_start, tier = price_of(m)
            rows.append((
                provider, model_id, is_vision(m), max_out, ctx,
                m.get("temperature") is False,
                m.get("tool_call") is True,
                m.get("reasoning") is True,
                efforts_of(m),
                thinking_control(m),
                base, tier_start, tier,
            ))

    rows.sort(key=lambda r: (r[0], r[1]))

    # 跨供应商兜底：只收 canonical_model_id（形如 vendor/model），
    # 用它的一家转售时，至少在模型表里没这行的时候还认得出这是哪个模型。
    canonical = {}
    for provider, p in data.items():
        for model_id, m in (p.get("models") or {}).items():
            key = (m.get("canonical_model_id") or "").strip().lower()
            if not key:
                continue
            ctx, max_out = windows(m)
            prev = canonical.get(key, (False, 0, 0))
            canonical[key] = (
                prev[0] or is_vision(m),
                max(prev[1], ctx),
                max(prev[2], max_out),
            )

    out = [
        "// <auto-generated>",
        "// Generated from models.dev/api.json by src/scripts/modelInfoGenerator/generate_modelsdev_capabilities.py.",
        "// Do not edit by hand; re-run the script instead.",
        "// </auto-generated>",
        "",
        "using UiharuMind.Core.AI.Models;",
        "using UiharuMind.Core.Configs.RemoteAI;",
        "",
        "namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;",
        "",
        "/// <summary>",
        "/// models.dev 静态能力表（构建期生成）。目录不携带能力字段时用它兜底，价格与思考档位只有这里有。",
        "/// 键是 <c>provider/modelId</c>；<see cref=\"CanonicalEntries\"/> 是按 <c>canonical_model_id</c> 的跨供应商兜底，",
        "/// 只解「这个模型是什么」（视觉与两个窗口），不解价格——转售商各自的挂牌价只能各查各的。",
        "/// </summary>",
        "internal enum ThinkingControl",
        "{",
        "    /// <summary>模型没声明 <c>reasoning_options</c>；不等于不支持思考，消费侧按兜底阶梯处理</summary>",
        "    Unknown,",
        "    /// <summary>有档位取值表，按名字选（<c>low</c>/<c>medium</c>/<c>xhigh</c>…）</summary>",
        "    Effort,",
        "    /// <summary>只有开/关，没有档位</summary>",
        "    Toggle,",
        "    /// <summary>只给 token 预算数值（Claude 风格），没有档位名</summary>",
        "    Budget,",
        "}",
        "",
        "internal static class ModelCapabilityTable",
        "{",
        "    internal readonly record struct Capability(",
        "        bool IsVision,",
        "        int MaxTokens,",
        "        int ContextLength,",
        "        bool OmitSamplingParams,",
        "        bool SupportsToolCall,",
        "        bool IsReasoning,",
        "        string Efforts,",
        "        ThinkingControl Thinking,",
        "        ModelPrice Price);",
        "",
        "    internal readonly record struct Canonical(bool IsVision, int ContextLength, int MaxTokens);",
        "",
        "    private static readonly Dictionary<string, Capability> Entries = new(StringComparer.OrdinalIgnoreCase)",
        "    {",
    ]
    for (provider, model_id, vision, max_out, ctx, omit, tool_call, reasoning,
         efforts, thinking, base, tier_start, tier) in rows:
        key = escape(f"{provider}/{model_id}")
        price = ", ".join([money(v) for v in base] + [str(tier_start)] + [money(v) for v in tier])
        out.append(
            f'        ["{key}"] = new({str(vision).lower()}, {max_out}, {ctx}, {str(omit).lower()}, '
            f'{str(tool_call).lower()}, {str(reasoning).lower()}, "{escape(efforts)}", '
            f'ThinkingControl.{thinking}, new ModelPrice({price})),'
        )

    out += [
        "    };",
        "",
        "    private static readonly Dictionary<string, Canonical> CanonicalEntries = new(StringComparer.OrdinalIgnoreCase)",
        "    {",
    ]
    for key in sorted(canonical):
        vision, ctx, max_out = canonical[key]
        out.append(f'        ["{escape(key)}"] = new({str(vision).lower()}, {ctx}, {max_out}),')
    out += [
        "    };",
        "",
        "    internal static bool TryGet(string providerKey, string modelId, out Capability capability) =>",
        '        Entries.TryGetValue(providerKey + "/" + modelId, out capability);',
        "",
        "    /// <summary>按 <c>canonical_model_id</c> 查；传进来的通常是转售商的原始模型 ID，两者常常同形</summary>",
        "    internal static bool TryGetCanonical(string modelId, out Canonical canonical) =>",
        "        CanonicalEntries.TryGetValue(modelId.Trim(), out canonical);",
        "",
        "    /// <summary>配置类型 → models.dev 的 provider 键；未知返回空串</summary>",
        "    internal static string ProviderKeyOf(Type configType) => configType switch",
        "    {",
    ]
    for type_name, provider in PROVIDER_TYPES:
        out.append(f'        _ when configType == typeof({type_name}) => "{provider}",')
    out += [
        '        _ => "",',
        "    };",
        "}",
        "",
    ]

    path = os.path.normpath(args.output)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out))
    print(f"wrote {path}: {len(rows)} entries, {len(canonical)} canonical")


if __name__ == "__main__":
    main()
