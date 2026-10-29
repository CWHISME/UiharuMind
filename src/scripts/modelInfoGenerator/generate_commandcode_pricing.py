#!/usr/bin/env python3
"""Generate ProviderPricingTable.g.cs from a provider's official pricing docs.

models.dev is a third-party snapshot and it lags: for CommandCode it has no row
at all for 23 of the 94 models the gateway serves (all the MiMo ones among
them), so price and vision both degrade to "unknown". The provider's own docs
are authoritative and list every model, so we scrape them at build time and
keep the result as an override layer above models.dev.

Two pages are joined: the models reference carries ``<code>model/id</code>``
plus capability flags, the pricing page carries the per-1M rates keyed by
display name. Ids never appear on the pricing page, so the join is by name —
with an explicit "unmatched" report, because a silent miss here means a wrong
number, not a missing one.

Safety: the script fails loudly (exit 1) when the pages stop parsing the way
this code expects, instead of emitting a half-empty table. Re-run it when the
provider adds models or changes prices; commit the .g.cs with the script.

Usage:
  python generate_commandcode_pricing.py
  python generate_commandcode_pricing.py --models-html a.html --pricing-html b.html
"""

import argparse
import datetime
import os
import re
import sys
import urllib.request

PROVIDER = "commandcode"
PROVIDER_TYPE = "RemoteCommandCodeModelConfig"
MODELS_URL = "https://commandcode.ai/docs/reference/cli/models"
PRICING_URL = "https://commandcode.ai/docs/resources/pricing-limits"

# 页面结构变了就别硬猜：低于这个数说明解析崩了，而不是模型变少了
MIN_MODELS = 80


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(request, timeout=60) as r:
        return r.read().decode("utf-8", "replace")


def read(path, url):
    if path:
        with open(path, encoding="utf-8") as f:
            return f.read()
    return fetch(url)


def plain(html):
    return re.sub(r"<[^>]+>", "", html).strip()


def norm(name):
    return re.sub(r"[^a-z0-9]", "", name.lower())


def parse_catalog(html):
    """模型清单页：<code>id</code> + 名称 + 能力（aria-label）。"""
    catalog = {}
    for row in re.findall(r"<tr>(.*?)</tr>", html, re.S):
        cells = re.findall(r"<td>(.*?)</td>", row, re.S)
        if len(cells) < 3:
            continue
        model_id = re.search(r"<code>(.*?)</code>", cells[0])
        if not model_id:
            continue
        name = plain(cells[1])
        caps = re.search(r'aria-label="Capabilities: ([^"]*)"', cells[2])
        catalog[model_id.group(1).strip()] = {
            "name": name,
            "caps": caps.group(1) if caps else "",
        }
    return catalog


def parse_pricing(html):
    """定价页：每个 role=row 是一行，7 列（名称/上下文/输入/输出/缓存读/缓存写/能力）。"""
    rows = []
    # 行内嵌套 div 很多，按「下一行从哪里开始」切块，别按闭合标签切（会切在名称格里）
    starts = [m.end() for m in re.finditer(
        r'<div class="grid grid-cols-\[[^"]*\] items-center" role="row">', html)]
    for index, start in enumerate(starts):
        block = html[start:starts[index + 1] if index + 1 < len(starts) else start + 4000]
        name = re.search(r'truncate text-\[13px\][^"]*"[^>]*>(.*?)</(?:a|span)>', block, re.S)
        if not name:
            continue
        # 列顺序：上下文、输入、输出、缓存读、缓存写。价格格有两种写法（普通行是纯文本，
        # 打折/免费行套了一层 flex flex-col），所以只认「px-2 py-3」这个共同的格标记
        cells = re.findall(r'<div class="[^"]*px-2 py-3[^"]*">(.*?)</div>', block, re.S)
        if len(cells) < 3:
            continue
        caps = re.search(r'aria-label="Capabilities: ([^"]*)"', block)
        rows.append({
            "name": plain(name.group(1)),
            "context": plain(cells[0]),
            "prices": [cell_value(cell) for cell in cells[1:5]],
            "caps": caps.group(1) if caps else "",
        })
    return rows


def cell_value(cell):
    """一格价格：Free / $0.60（折扣行是「原价 + 现价」，取最后一个）/ — 。"""
    text = plain(cell)
    if "free" in text.lower():
        return "free"
    values = re.findall(r"\$([0-9]+(?:\.[0-9]+)?)", text)
    return values[-1] if values else ""


def parse_context(text):
    """1M / 256K / — → token 数。"""
    m = re.match(r"([0-9.]+)\s*([KkMm])", text.strip())
    if not m:
        return 0
    scale = 1024 if m.group(2).lower() == "k" else 1024 * 1024
    return int(float(m.group(1)) * scale)


def money(value):
    if value == "" or value == "free":
        return "0m"
    text = f"{float(value):.6f}".rstrip("0").rstrip(".")
    return f"{text}m"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--models-html", help="本地保存的模型清单页；缺省走网络")
    parser.add_argument("--pricing-html", help="本地保存的定价页；缺省走网络")
    parser.add_argument(
        "--output",
        default=os.path.join(
            os.path.dirname(__file__), "..", "..", "UiharuMind.Core", "RemoteOpenAI", "ModelCatalog",
            "ProviderPricingTable.g.cs",
        ),
    )
    args = parser.parse_args()

    catalog = parse_catalog(read(args.models_html, MODELS_URL))
    pricing = parse_pricing(read(args.pricing_html, PRICING_URL))
    if len(catalog) < MIN_MODELS or len(pricing) < MIN_MODELS:
        print(f"parse looks broken: {len(catalog)} models, {len(pricing)} price rows "
              f"(expected >= {MIN_MODELS}) — page structure changed?", file=sys.stderr)
        sys.exit(1)

    # 同名会出现两次（免费版与付费版同显示名，如 MiniMax M3.1 Flash），取有价的那一行
    by_name = {}
    for row in pricing:
        key = norm(row["name"])
        current = by_name.get(key)
        if current is None or (current["prices"][0] in ("", "free") and row["prices"][0] not in ("", "free")):
            by_name[key] = row

    entries = []
    unmatched = []
    for model_id, info in sorted(catalog.items()):
        row = by_name.get(norm(info["name"]))
        if row is None:
            unmatched.append(model_id)
            continue
        is_free = "free" in (row["caps"] + row["prices"][0]).lower() or row["prices"][0] == "free"
        is_vision = "vision" in (info["caps"] + row["caps"]).lower()
        prices = row["prices"]
        entries.append((
            model_id,
            is_vision,
            parse_context(row["context"]),
            is_free,
            [money(v) for v in prices],
        ))

    today = datetime.date.today().isoformat()
    out = [
        "// <auto-generated>",
        f"// Generated from {PROVIDER} docs by src/scripts/modelInfoGenerator/generate_commandcode_pricing.py.",
        f"// Sources: {MODELS_URL} + {PRICING_URL}",
        f"// Fetched: {today}. Do not edit by hand; re-run the script instead.",
        "// </auto-generated>",
        "",
        "using UiharuMind.Core.AI.Models;",
        "using UiharuMind.Core.Configs.RemoteAI;",
        "",
        "namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;",
        "",
        "/// <summary>",
        "/// 供应商官网定价页的<b>覆盖层</b>：权威于 models.dev 的第三方快照（后者会缺行、缺价）。",
        "/// 只收官网真的列出来的模型；<c>IsVision</c> 由能力徽标得来，<c>IsFree</c> 表示官网明写免费。",
        "/// 查询优先级见 <see cref=\"ModelCapabilityResolver\"/>：目录声明 &gt; 本表 &gt; models.dev 表。",
        "/// </summary>",
        "internal static class ProviderPricingTable",
        "{",
        "    internal readonly record struct PriceRow(bool IsVision, int ContextLength, ModelPrice Price);",
        "",
        "    private static readonly Dictionary<string, PriceRow> Entries = new(StringComparer.OrdinalIgnoreCase)",
        "    {",
    ]
    for model_id, is_vision, context_length, is_free, prices in entries:
        key = f"{PROVIDER}/{model_id}".replace("\\", "\\\\").replace('"', '\\"')
        price_args = ", ".join(prices + ["0", "0m", "0m", "0m", "0m"])
        free_arg = ", IsFree: true" if is_free else ""
        out.append(
            f'        ["{key}"] = new({str(is_vision).lower()}, {context_length}, '
            f'new ModelPrice({price_args}{free_arg})),'
        )
    out += [
        "    };",
        "",
        "    internal static bool TryGet(string providerKey, string modelId, out PriceRow row) =>",
        '        Entries.TryGetValue(providerKey + "/" + modelId, out row);',
        "",
        "    /// <summary>供应商官网定价页的抓取日期，界面标注数据出处用</summary>",
        f'    internal const string FetchedOn = "{today}";',
        "}",
        "",
    ]

    path = os.path.normpath(args.output)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out))

    print(f"wrote {path}: {len(entries)} rows (catalog {len(catalog)}, pricing {len(pricing)})")
    if unmatched:
        print(f"unmatched {len(unmatched)} ids (no price row by name):", file=sys.stderr)
        for model_id in unmatched:
            print(f"  - {model_id}  ({catalog[model_id]['name']})", file=sys.stderr)


if __name__ == "__main__":
    main()
